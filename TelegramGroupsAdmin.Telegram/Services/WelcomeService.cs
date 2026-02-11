using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Models.Welcome;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Core.JobPayloads;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using static TelegramGroupsAdmin.Core.BackgroundJobs.DeduplicationKeys;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Moderation;
using TelegramGroupsAdmin.Telegram.Services.Welcome;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Handles the welcome flow for new users joining chats.
/// Scoped service with direct dependency injection.
/// </summary>
public class WelcomeService(
    IConfigService configService,
    IWelcomeResponsesRepository welcomeResponsesRepository,
    ITelegramUserRepository telegramUserRepository,
    IExamFlowService examFlowService,
    IImpersonationDetectionService impersonationDetectionService,
    IBotProtectionService botProtectionService,
    IBotDmService dmDeliveryService,
    IBotMessageService messageService,
    IBotUserService userService,
    IBotChatService chatService,
    IBotModerationService moderationService,
    IJobScheduler jobScheduler,
    ICasCheckService casCheckService,
    TelegramPhotoService photoService,
    ILogger<WelcomeService> logger) : IWelcomeService
{
    // Deletion source constants for audit tracking
    private const string DeletionSourceWelcomeCleanup = "welcome_cleanup";
    private const string DeletionSourceWelcomeError = "welcome_error_cleanup";

    // Reason constants for moderation audit
    private const string ReasonDeniedRules = "Denied rules during welcome flow";
    private const string ReasonPendingVerification = "Pending welcome verification";
    private const string ReasonSecurityPassed = "Security checks passed (welcome disabled)";
    private const string ReasonCompletedWelcome = "Completed welcome/rules flow";
    private const string ReasonCompletedWelcomeDm = "Completed welcome flow via DM";

    // User-facing message constants
    private const string ErrorNoWelcomeRecord = "❌ Could not find your welcome record. Please try accepting in the group chat instead.";
    private const string ErrorPermissionsFailed = "❌ Failed to restore your permissions. Please contact an admin.";


    public async Task HandleChatMemberUpdateAsync(
        ChatMemberUpdated chatMemberUpdate,
        CancellationToken cancellationToken)
    {
        // Detect new user joins (status changed to Member)
        var oldStatus = chatMemberUpdate.OldChatMember.Status;
        var newStatus = chatMemberUpdate.NewChatMember.Status;
        var user = chatMemberUpdate.NewChatMember.User;

        // Handle user leaving (Member/Restricted → Left)
        if (oldStatus is ChatMemberStatus.Member or ChatMemberStatus.Restricted &&
            newStatus is ChatMemberStatus.Left or ChatMemberStatus.Kicked)
        {
            await HandleUserLeftAsync(chatMemberUpdate.Chat, user, cancellationToken);
            return;
        }

        // Only handle new joins (Left/Kicked → Member)
        if (newStatus != ChatMemberStatus.Member ||
            (oldStatus != ChatMemberStatus.Left && oldStatus != ChatMemberStatus.Kicked))
        {
            return;
        }

        // Phase 6.1: Bot Protection - check if bot should be allowed
        if (user.IsBot)
        {
            var shouldAllow = await botProtectionService.ShouldAllowBotAsync(
                chatMemberUpdate.Chat,
                user,
                chatMemberUpdate);

            if (!shouldAllow)
            {
                // Ban the bot
                await botProtectionService.BanBotAsync(
                    chatMemberUpdate.Chat,
                    user,
                    "Not whitelisted and not invited by admin",
                    cancellationToken);
                return;
            }

            // Bot is allowed (whitelisted or admin-invited) - skip welcome message
            logger.LogDebug("Skipping welcome for allowed bot {User}", user.ToLogDebug());
            return;
        }

        logger.LogInformation(
            "New user joined: {User} in {Chat}",
            user.ToLogInfo(),
            chatMemberUpdate.Chat.ToLogInfo());

        // Load welcome config from database (chat-specific or global fallback)
        var config = await configService.GetEffectiveAsync<WelcomeConfig>(ConfigType.Welcome, chatMemberUpdate.Chat.Id)
                     ?? WelcomeConfig.Default;

        // Track message ID for cleanup on ban/security failure
        int? verifyingMessageId = null;

        try
        {
            // Step 1: Check if user is an admin/owner - skip all checks for admins
            var chatMember = await userService.GetChatMemberAsync(chatMemberUpdate.Chat.Id, user.Id, cancellationToken);
            if (chatMember.Status is ChatMemberStatus.Administrator or ChatMemberStatus.Creator)
            {
                logger.LogInformation(
                    "Skipping welcome for admin/owner: {User} in {Chat}",
                    user.ToLogInfo(),
                    chatMemberUpdate.Chat.ToLogInfo());
                return;
            }

            // Step 2: Create user record if not exists (MUST happen before any moderation
            // actions, because audit logging has a FK constraint on telegram_users)
            var existingUser = await telegramUserRepository.GetByTelegramIdAsync(user.Id, cancellationToken);

            if (existingUser == null)
            {
                var now = DateTimeOffset.UtcNow;
                var newUser = new Models.TelegramUser(
                    TelegramUserId: user.Id,
                    Username: user.Username,
                    FirstName: user.FirstName,
                    LastName: user.LastName,
                    UserPhotoPath: null,
                    PhotoHash: null,
                    PhotoFileUniqueId: null,
                    IsBot: user.IsBot,
                    IsTrusted: false,
                    IsBanned: false,
                    BotDmEnabled: false,
                    FirstSeenAt: now,
                    LastSeenAt: now,
                    CreatedAt: now,
                    UpdatedAt: now,
                    IsActive: false // Inactive until welcome accepted or message sent
                );
                await telegramUserRepository.UpsertAsync(newUser, cancellationToken);
                existingUser = newUser;

                logger.LogDebug(
                    "Created inactive user record for {User} on join",
                    user.ToLogDebug());
            }

            // Step 3: Restrict user permissions (mute immediately - no spam window)
            await RestrictUserPermissionsAsync(chatMemberUpdate.Chat, user, cancellationToken);

            // Step 4: Send verifying message
            var username = TelegramDisplayName.FormatMention(user);
            var verifyingText = WelcomeMessageBuilder.FormatVerifyingMessage(username);
            var verifyingMessage = await messageService.SendAndSaveMessageAsync(
                chatId: chatMemberUpdate.Chat.Id,
                text: verifyingText,
                cancellationToken: cancellationToken);
            verifyingMessageId = verifyingMessage.MessageId;

            // ═══════════════════════════════════════════════════════════════════
            // SECURITY CHECKS (always run regardless of config.Enabled)
            // Order: CAS (fail fast) → Photo fetch → Impersonation (uses photo)
            // ═══════════════════════════════════════════════════════════════════

            // Step 5: CAS (Combot Anti-Spam) check - auto-ban known spammers FIRST (fail fast)
            if (config.JoinSecurity.Cas.Enabled)
            {
                var casResult = await casCheckService.CheckUserAsync(user.Id, config.JoinSecurity.Cas, cancellationToken);
                if (casResult.IsBanned)
                {
                    logger.LogWarning(
                        "CAS banned user detected: {User} in {Chat} (reason: {Reason})",
                        user.ToLogInfo(),
                        chatMemberUpdate.Chat.ToLogInfo(),
                        casResult.Reason ?? "No reason provided");

                    // Delete verifying message before ban
                    await TryDeleteMessageAsync(chatMemberUpdate.Chat.Id, verifyingMessageId.Value, cancellationToken);

                    // Ban user using moderationService (triggers ban celebrations)
                    var reason = $"CAS banned: {casResult.Reason ?? "Listed in CAS database"}";
                    await moderationService.BanUserAsync(
                        new BanIntent
                        {
                            User = UserIdentity.From(user),
                            Executor = Actor.Cas,
                            Reason = reason,
                            Chat = ChatIdentity.From(chatMemberUpdate.Chat)
                        },
                        cancellationToken);

                    logger.LogInformation(
                        "{User} auto-banned (CAS), skipping welcome flow",
                        user.ToLogInfo());
                    return;
                }
            }
            else
            {
                logger.LogDebug("CAS check disabled, skipping for {User}", user.ToLogDebug());
            }

            // Step 6: Photo fetch (sync, ~1.8s) - enables full impersonation detection
            string? userPhotoPath = existingUser.UserPhotoPath;
            var photoResult = await photoService.GetUserPhotoWithMetadataAsync(
                user.Id,
                knownPhotoId: existingUser.PhotoFileUniqueId,
                existingUser,
                cancellationToken);

            if (photoResult != null)
            {
                userPhotoPath = photoResult.RelativePath;

                // Update user record with photo path and FileUniqueId for smart caching
                await telegramUserRepository.UpdatePhotoFileUniqueIdAsync(
                    user.Id,
                    photoResult.FileUniqueId,
                    photoResult.RelativePath,
                    cancellationToken);

                logger.LogDebug(
                    "Fetched profile photo for {User}: {Path}",
                    user.ToLogDebug(),
                    photoResult.RelativePath);
            }

            // Step 7: Impersonation detection (now has photo for full capability)
            if (config.JoinSecurity.Impersonation.Enabled)
            {
                var shouldCheck = await impersonationDetectionService.ShouldCheckUserAsync(user.Id, chatMemberUpdate.Chat.Id);

                if (shouldCheck)
                {
                    logger.LogDebug(
                        "Checking {User} for impersonation in {Chat}",
                        user.ToLogDebug(),
                        chatMemberUpdate.Chat.ToLogDebug());

                    var impersonationResult = await impersonationDetectionService.CheckUserAsync(
                        user,
                        chatMemberUpdate.Chat,
                        userPhotoPath);

                    if (impersonationResult != null)
                    {
                        logger.LogWarning(
                            "Impersonation detected for {User} in {Chat} (score: {Score}, risk: {Risk})",
                            user.ToLogDebug(),
                            chatMemberUpdate.Chat.ToLogDebug(),
                            impersonationResult.TotalScore,
                            impersonationResult.RiskLevel);

                        // Execute action (create alert, auto-ban if score >= 100)
                        await impersonationDetectionService.ExecuteActionAsync(impersonationResult);

                        // If auto-banned (score 100), clean up and exit
                        if (impersonationResult.ShouldAutoBan)
                        {
                            // Delete verifying message
                            await TryDeleteMessageAsync(chatMemberUpdate.Chat.Id, verifyingMessageId.Value, cancellationToken);

                            logger.LogInformation(
                                "{User} auto-banned for impersonation, skipping welcome flow",
                                user.ToLogInfo());
                            return;
                        }

                        // Score 50-99: Continue with welcome flow (alert created for manual review)
                        logger.LogInformation(
                            "{User} flagged for impersonation review (score: {Score}), continuing with welcome flow",
                            user.ToLogInfo(),
                            impersonationResult.TotalScore);
                    }
                }
            }
            else
            {
                logger.LogDebug("Impersonation detection disabled, skipping for {User}", user.ToLogDebug());
            }

            // ═══════════════════════════════════════════════════════════════════
            // SECURITY CHECKS PASSED - Branch based on welcome config
            // ═══════════════════════════════════════════════════════════════════

            if (!config.Enabled)
            {
                // Welcome DISABLED: Security checks passed, unmute and clean up
                logger.LogDebug(
                    "Welcome system disabled for {Chat}, security passed - unmuting {User}",
                    chatMemberUpdate.Chat.ToLogDebug(),
                    user.ToLogDebug());

                // Restore user permissions via moderation service
                await moderationService.RestoreUserPermissionsAsync(
                    new RestorePermissionsIntent
                    {
                        User = UserIdentity.From(user),
                        Chat = ChatIdentity.From(chatMemberUpdate.Chat),
                        Executor = Actor.WelcomeFlow,
                        Reason = ReasonSecurityPassed
                    },
                    cancellationToken);

                // Mark user as active (security passed, no welcome flow)
                await telegramUserRepository.SetActiveAsync(user.Id, true, cancellationToken);

                // Delete verifying message
                await TryDeleteMessageAsync(chatMemberUpdate.Chat.Id, verifyingMessageId.Value, cancellationToken);

                logger.LogInformation(
                    "{User} passed security checks in {Chat} (welcome disabled)",
                    user.ToLogInfo(),
                    chatMemberUpdate.Chat.ToLogInfo());
                return;
            }

            // Welcome ENABLED: Update verifying message to full welcome content
            var chatInfo = await chatService.GetChatAsync(chatMemberUpdate.Chat.Id, cancellationToken);
            var chatName = chatInfo.Title ?? "this chat";
            var messageText = WelcomeMessageBuilder.FormatWelcomeMessage(config, username, chatName);

            // Build keyboard based on welcome mode
            InlineKeyboardMarkup keyboard;
            if (config.Mode == WelcomeMode.DmWelcome)
            {
                var botInfo = await userService.GetMeAsync(cancellationToken);
                keyboard = WelcomeKeyboardBuilder.BuildDmModeKeyboard(config, chatMemberUpdate.Chat.Id, user.Id, botInfo.Username!);
            }
            else if (config.Mode == WelcomeMode.EntranceExam)
            {
                var botInfo = await userService.GetMeAsync(cancellationToken);
                keyboard = WelcomeKeyboardBuilder.BuildExamModeKeyboard(config, chatMemberUpdate.Chat.Id, user.Id, botInfo.Username!);
            }
            else
            {
                keyboard = WelcomeKeyboardBuilder.BuildChatModeKeyboard(config, user.Id);
            }

            // Update the verifying message to become the welcome message
            await messageService.EditAndUpdateMessageAsync(
                chatId: chatMemberUpdate.Chat.Id,
                messageId: verifyingMessageId.Value,
                text: messageText,
                replyMarkup: keyboard,
                cancellationToken: cancellationToken);

            var welcomeMessageId = verifyingMessageId.Value;

            logger.LogDebug(
                "Updated verifying message to welcome for {User} in {Chat} (mode: {Mode})",
                user.ToLogDebug(),
                chatMemberUpdate.Chat.ToLogDebug(),
                config.Mode);

            // Step 8: Create welcome response record (pending state)
            var welcomeResponse = new WelcomeResponse(
                Id: 0, // Will be set by database
                ChatId: chatMemberUpdate.Chat.Id,
                UserId: user.Id,
                Username: user.Username,
                WelcomeMessageId: welcomeMessageId,
                Response: WelcomeResponseType.Pending,
                RespondedAt: DateTimeOffset.UtcNow,
                DmSent: false,
                DmFallback: false,
                CreatedAt: DateTimeOffset.UtcNow,
                TimeoutJobId: null // Will be set after scheduling job
            );

            var responseId = await welcomeResponsesRepository.InsertAsync(welcomeResponse, cancellationToken);

            // Step 9: Schedule timeout via Quartz.NET
            var payload = new WelcomeTimeoutPayload(
                UserIdentity.From(user),
                ChatIdentity.From(chatMemberUpdate.Chat),
                welcomeMessageId
            );

            var jobId = await jobScheduler.ScheduleJobAsync(
                "WelcomeTimeout",
                payload,
                delaySeconds: config.TimeoutSeconds,
                deduplicationKey: None,
                cancellationToken);

            // Store the job ID in the welcome response record
            await welcomeResponsesRepository.SetTimeoutJobIdAsync(responseId, jobId, cancellationToken);

            logger.LogDebug(
                "Successfully scheduled welcome timeout for {User} in {Chat} (mode: {Mode}, timeout: {Timeout}s, JobId: {JobId})",
                user.ToLogDebug(),
                chatMemberUpdate.Chat.ToLogDebug(),
                config.Mode,
                config.TimeoutSeconds,
                jobId);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to process welcome for {User} in {Chat}",
                user.ToLogDebug(),
                chatMemberUpdate.Chat.ToLogDebug());

            // Try to clean up verifying message on error
            if (verifyingMessageId.HasValue)
            {
                try
                {
                    await messageService.DeleteAndMarkMessageAsync(chatMemberUpdate.Chat.Id, verifyingMessageId.Value, DeletionSourceWelcomeError, cancellationToken);
                }
                catch (Exception cleanupEx)
                {
                    logger.LogDebug(cleanupEx, "Failed to clean up verifying message {MessageId} in {Chat}",
                        verifyingMessageId.Value, chatMemberUpdate.Chat.ToLogDebug());
                }
            }
        }
    }

    /// <summary>
    /// Helper to delete a message without throwing on failure.
    /// Used for cleanup during security check failures.
    /// </summary>
    private async Task TryDeleteMessageAsync(long chatId, int messageId, CancellationToken cancellationToken)
    {
        try
        {
            await messageService.DeleteAndMarkMessageAsync(chatId, messageId, DeletionSourceWelcomeCleanup, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to delete message {MessageId} (non-fatal)", messageId);
        }
    }

    public async Task HandleCallbackQueryAsync(
        CallbackQuery callbackQuery,
        CancellationToken cancellationToken)
    {
        var data = callbackQuery.Data;
        var user = callbackQuery.From;
        var message = callbackQuery.Message;

        if (message == null || string.IsNullOrEmpty(data))
        {
            logger.LogWarning("Callback query missing message or data");
            return;
        }

        var chatId = message.Chat.Id;

        // Check if this is an exam callback (handled separately)
        var isExamCallback = examFlowService.IsExamCallback(data);

        if (isExamCallback)
        {
            await HandleExamCallbackAsync(callbackQuery, data, user, message, cancellationToken);
            return;
        }

        logger.LogDebug(
            "Callback query received: {Data} from {User} in {Chat}",
            data,
            user.ToLogDebug(),
            message.Chat.ToLogDebug());

        // Use extracted parser for callback data
        var parsedCallback = WelcomeCallbackParser.ParseCallbackData(data);
        if (parsedCallback == null)
        {
            logger.LogWarning("Invalid or unrecognized callback data format: {Data}", data);
            return;
        }

        // Validate that the clicking user is the target user
        if (!WelcomeCallbackParser.ValidateCallerIsTarget(user.Id, parsedCallback.UserId))
        {
            logger.LogWarning(
                "Wrong user clicked button: {Clicker} clicked button for target user {TargetUserId}",
                user.ToLogDebug(),
                parsedCallback.UserId);

            // For DM accept, just show alert (no message in DM chat)
            if (parsedCallback.Type == WelcomeCallbackType.DmAccept)
            {
                await messageService.AnswerCallbackAsync(
                    callbackQueryId: callbackQuery.Id,
                    text: "⚠️ This button is not for you.",
                    cancellationToken: cancellationToken);
                return;
            }

            // For chat buttons, send temporary warning message
            await SendWrongUserWarningAsync(chatId, user, message.MessageId, cancellationToken);
            return;
        }

        // Route to appropriate handler based on callback type
        try
        {
            switch (parsedCallback.Type)
            {
                case WelcomeCallbackType.DmAccept:
                    await HandleDmAcceptAsync(
                        parsedCallback.ChatId!.Value,
                        user,
                        message.Chat.Id,
                        message.MessageId,
                        cancellationToken);
                    break;

                case WelcomeCallbackType.Accept:
                case WelcomeCallbackType.Deny:
                    // Load welcome config for chat-based callbacks
                    var config = await configService.GetEffectiveAsync<WelcomeConfig>(ConfigType.Welcome, chatId)
                                 ?? WelcomeConfig.Default;

                    if (parsedCallback.Type == WelcomeCallbackType.Accept)
                    {
                        await HandleAcceptAsync(message.Chat, user, message.MessageId, config, cancellationToken);
                    }
                    else
                    {
                        await HandleDenyAsync(message.Chat, user, message.MessageId, cancellationToken);
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to handle callback {Data} for {User} in {Chat}",
                data,
                user.ToLogDebug(),
                message.Chat.ToLogDebug());
        }
    }

    private async Task SendWrongUserWarningAsync(
        long chatId,
        User user,
        int replyToMessageId,
        CancellationToken cancellationToken)
    {
        try
        {
            var username = TelegramDisplayName.FormatMention(user);
            var warningText = WelcomeMessageBuilder.FormatWrongUserWarning(username);
            var warningMsg = await messageService.SendAndSaveMessageAsync(
                chatId: chatId,
                text: warningText,
                replyParameters: new ReplyParameters { MessageId = replyToMessageId },
                cancellationToken: cancellationToken);

            // Delete warning after 10 seconds via Quartz.NET
            var deletePayload = new DeleteMessagePayload(
                chatId,
                warningMsg.MessageId,
                "wrong_user_warning"
            );

            await jobScheduler.ScheduleJobAsync(
                "DeleteMessage",
                deletePayload,
                delaySeconds: 10,
                deduplicationKey: None,
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send warning message");
        }
    }

    private async Task HandleExamCallbackAsync(
        CallbackQuery callbackQuery,
        string data,
        User user,
        Message message,
        CancellationToken cancellationToken)
    {
        logger.LogDebug(
            "Exam callback received: {Data} from {User}",
            data,
            user.ToLogDebug());

        try
        {
            // Parse callback and handle via ExamFlowService
            var parsed = examFlowService.ParseExamCallback(data);
            if (parsed == null)
            {
                logger.LogWarning("Failed to parse exam callback: {Data}", data);
                return;
            }

            var (sessionId, questionIndex, answerIndex) = parsed.Value;

            var result = await examFlowService.HandleMcAnswerAsync(
                sessionId,
                questionIndex,
                answerIndex,
                user,
                message,
                cancellationToken);

            if (result.ExamComplete && result.GroupChatId.HasValue)
            {
                logger.LogInformation(
                    "Exam completed for {User}: Passed={Passed}, SentToReview={SentToReview}",
                    user.ToLogInfo(),
                    result.Passed,
                    result.SentToReview);

                // Cancel welcome timeout job if exam completed
                // Use GroupChatId from result (not message.Chat.Id which is the DM chat)
                var welcomeResponse = await welcomeResponsesRepository.GetByUserAndChatAsync(
                    user.Id, result.GroupChatId.Value, cancellationToken);

                if (welcomeResponse?.TimeoutJobId != null)
                {
                    await jobScheduler.CancelJobAsync(welcomeResponse.TimeoutJobId, cancellationToken);
                    await welcomeResponsesRepository.SetTimeoutJobIdAsync(
                        welcomeResponse.Id, null, cancellationToken);
                }

                // Update welcome response based on exam result
                if (result.Passed == true)
                {
                    await welcomeResponsesRepository.UpdateResponseAsync(
                        welcomeResponse!.Id, WelcomeResponseType.Accepted, dmSent: false, dmFallback: false, cancellationToken);
                }
                else if (result.SentToReview)
                {
                    // Keep as pending - admin will decide
                    logger.LogInformation(
                        "{User} failed exam and sent to review queue",
                        user.ToLogInfo());
                }
            }

            // Answer callback to clear loading state
            await messageService.AnswerCallbackAsync(callbackQuery.Id, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to handle exam callback {Data}", data);
        }
    }

    private async Task RestrictUserPermissionsAsync(
        Chat chat,
        User user,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Use a long duration (365 days) - the welcome timeout job controls the actual timeout
            var result = await moderationService.RestrictUserAsync(
                new RestrictIntent
                {
                    User = UserIdentity.From(user),
                    Executor = Actor.WelcomeFlow,
                    Reason = ReasonPendingVerification,
                    Duration = TimeSpan.FromDays(365),
                    Chat = ChatIdentity.From(chat)
                },
                cancellationToken);

            if (!result.Success)
            {
                logger.LogWarning(
                    "Failed to restrict {User} in {Chat}: {Error}",
                    user.ToLogDebug(),
                    chat.ToLogDebug(),
                    result.ErrorMessage);
            }
            else
            {
                logger.LogDebug(
                    "Restricted permissions for {User} in {Chat}",
                    user.ToLogDebug(),
                    chat.ToLogDebug());
            }
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to restrict {User} in {Chat}",
                user.ToLogDebug(),
                chat.ToLogDebug());
            throw;
        }
    }

    private async Task KickUserAsync(
        Chat chat,
        User user,
        string reason,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await moderationService.KickUserFromChatAsync(
                new KickIntent
                {
                    User = UserIdentity.From(user),
                    Chat = ChatIdentity.From(chat),
                    Executor = Actor.WelcomeFlow,
                    Reason = reason
                },
                cancellationToken);

            if (!result.Success)
            {
                logger.LogWarning(
                    "Failed to kick {User} from {Chat}: {Error}",
                    user.ToLogDebug(),
                    chat.ToLogDebug(),
                    result.ErrorMessage);
            }
            else
            {
                logger.LogDebug(
                    "Kicked {User} from {Chat}",
                    user.ToLogDebug(),
                    chat.ToLogDebug());
            }
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to kick {User} from {Chat}",
                user.ToLogDebug(),
                chat.ToLogDebug());
            throw;
        }
    }

    private async Task HandleAcceptAsync(
        Chat chat,
        User user,
        int welcomeMessageId,
        WelcomeConfig config,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "{User} accepted rules in {Chat}",
            user.ToLogInfo(),
            chat.ToLogInfo());

        // Step 1: Check if user already responded (from pending record created on join)
        var existingResponse = await welcomeResponsesRepository.GetByUserAndChatAsync(user.Id, chat.Id, cancellationToken);

        // Step 2: Cancel timeout job if it exists
        if (existingResponse?.TimeoutJobId != null)
        {
            if (await jobScheduler.CancelJobAsync(existingResponse.TimeoutJobId, cancellationToken))
            {
                // Clear the job ID since it's been cancelled
                await welcomeResponsesRepository.SetTimeoutJobIdAsync(existingResponse.Id, null, cancellationToken);
            }
        }

        // Step 3: Try to send rules via DM (or fallback to chat)
        // Always attempt this - previous DM sent via /start may have been deleted by user
        var (dmSent, dmFallback) = await SendRulesAsync(chat, user, config, cancellationToken);

        logger.LogDebug(
            "Rules delivery for {User}: DM sent: {DmSent}, Fallback: {DmFallback}",
            user.ToLogDebug(),
            dmSent,
            dmFallback);

        // Step 4: Restore user permissions via moderation service (audit trail)
        var restoreResult = await moderationService.RestoreUserPermissionsAsync(
            new RestorePermissionsIntent
            {
                User = UserIdentity.From(user),
                Chat = ChatIdentity.From(chat),
                Executor = Actor.WelcomeFlow,
                Reason = ReasonCompletedWelcome
            },
            cancellationToken);

        if (!restoreResult.Success)
        {
            logger.LogWarning(
                "Failed to restore permissions for {User} in {Chat}: {Error}",
                user.ToLogInfo(),
                chat.ToLogInfo(),
                restoreResult.ErrorMessage);
        }

        // Step 4b: Mark user as active (completed welcome flow)
        await telegramUserRepository.SetActiveAsync(user.Id, true, cancellationToken);

        // Step 5: Delete welcome message
        await TryDeleteMessageAsync(chat.Id, welcomeMessageId, cancellationToken);

        // Step 6: Update or create response record
        if (existingResponse != null)
        {
            // Update existing record
            await welcomeResponsesRepository.UpdateResponseAsync(existingResponse.Id, WelcomeResponseType.Accepted, dmSent, dmFallback, cancellationToken);
        }
        else
        {
            // Create new record (shouldn't happen normally, but handle it)
            var newResponse = new WelcomeResponse(
                Id: 0,
                ChatId: chat.Id,
                UserId: user.Id,
                Username: user.Username,
                WelcomeMessageId: welcomeMessageId,
                Response: WelcomeResponseType.Accepted,
                RespondedAt: DateTimeOffset.UtcNow,
                DmSent: dmSent,
                DmFallback: dmFallback,
                CreatedAt: DateTimeOffset.UtcNow,
                TimeoutJobId: null
            );
            await welcomeResponsesRepository.InsertAsync(newResponse, cancellationToken);
        }
    }

    private async Task HandleDenyAsync(
        Chat chat,
        User user,
        int welcomeMessageId,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "{User} denied rules in {Chat}",
            user.ToLogInfo(),
            chat.ToLogInfo());

        // Step 1: Cancel timeout job if it exists
        var existingResponse = await welcomeResponsesRepository.GetByUserAndChatAsync(user.Id, chat.Id, cancellationToken);
        if (existingResponse?.TimeoutJobId != null)
        {
            if (await jobScheduler.CancelJobAsync(existingResponse.TimeoutJobId, cancellationToken))
            {
                await welcomeResponsesRepository.SetTimeoutJobIdAsync(existingResponse.Id, null, cancellationToken);
            }
        }

        // Step 2: Kick user
        await KickUserAsync(chat, user, ReasonDeniedRules, cancellationToken);

        // Step 3: Delete welcome message
        await TryDeleteMessageAsync(chat.Id, welcomeMessageId, cancellationToken);

        // Step 4: Update or create response record
        if (existingResponse != null)
        {
            await welcomeResponsesRepository.UpdateResponseAsync(existingResponse.Id, WelcomeResponseType.Denied, dmSent: false, dmFallback: false, cancellationToken);
        }
        else
        {
            var newResponse = new WelcomeResponse(
                Id: 0,
                ChatId: chat.Id,
                UserId: user.Id,
                Username: user.Username,
                WelcomeMessageId: welcomeMessageId,
                Response: WelcomeResponseType.Denied,
                RespondedAt: DateTimeOffset.UtcNow,
                DmSent: false,
                DmFallback: false,
                CreatedAt: DateTimeOffset.UtcNow,
                TimeoutJobId: null
            );
            await welcomeResponsesRepository.InsertAsync(newResponse, cancellationToken);
        }
    }

    private async Task HandleDmAcceptAsync(
        long groupChatId,
        User user,
        long dmChatId,
        int buttonMessageId,
        CancellationToken cancellationToken = default)
    {
        // Fetch group chat info early for logging throughout the method
        var groupChat = await chatService.GetChatAsync(groupChatId, cancellationToken);

        logger.LogInformation(
            "{User} accepted rules via DM for {Chat}",
            user.ToLogInfo(),
            groupChat.ToLogInfo());

        // Step 1: Delete the Accept button message in DM (separate message from rules)
        try
        {
            await dmDeliveryService.DeleteDmMessageAsync(dmChatId, buttonMessageId, cancellationToken);

            logger.LogDebug(
                "Deleted DM Accept button message {MessageId} for {User}",
                buttonMessageId,
                user.ToLogDebug());
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to delete DM Accept button message {MessageId}",
                buttonMessageId);
            // Non-fatal - continue with acceptance flow
        }

        // Step 2: Find the welcome response record
        var welcomeResponse = await welcomeResponsesRepository.GetByUserAndChatAsync(user.Id, groupChatId, cancellationToken);

        if (welcomeResponse == null)
        {
            logger.LogWarning(
                "No welcome response found for {User} in {Chat}",
                user.ToLogDebug(),
                groupChat.ToLogDebug());

            // Send error to user in DM
            await dmDeliveryService.SendDmAsync(user.Id, ErrorNoWelcomeRecord, cancellationToken: cancellationToken);
            return;
        }

        // Step 3: Cancel timeout job if it exists
        if (welcomeResponse.TimeoutJobId != null)
        {
            if (await jobScheduler.CancelJobAsync(welcomeResponse.TimeoutJobId, cancellationToken))
            {
                await welcomeResponsesRepository.SetTimeoutJobIdAsync(welcomeResponse.Id, null, cancellationToken);
            }
        }

        // Step 4: Restore user permissions in group via moderation service (audit trail)
        var restoreResult = await moderationService.RestoreUserPermissionsAsync(
            new RestorePermissionsIntent
            {
                User = UserIdentity.From(user),
                Chat = ChatIdentity.From(groupChat),
                Executor = Actor.WelcomeFlow,
                Reason = ReasonCompletedWelcomeDm
            },
            cancellationToken);

        if (!restoreResult.Success)
        {
            logger.LogError(
                "Failed to restore permissions for {User} in {Chat}: {Error}",
                user.ToLogInfo(),
                groupChat.ToLogInfo(),
                restoreResult.ErrorMessage);

            // Send error to user in DM
            await dmDeliveryService.SendDmAsync(user.Id, ErrorPermissionsFailed, cancellationToken: cancellationToken);
            return;
        }

        // Step 4b: Mark user as active (completed welcome flow via DM)
        await telegramUserRepository.SetActiveAsync(user.Id, true, cancellationToken);

        // Step 5: Delete welcome message in group
        await TryDeleteMessageAsync(groupChatId, welcomeResponse.WelcomeMessageId, cancellationToken);

        // Step 6: Update welcome response record (mark as accepted via DM)
        await welcomeResponsesRepository.UpdateResponseAsync(welcomeResponse.Id, WelcomeResponseType.Accepted, dmSent: true, dmFallback: false, cancellationToken);

        // Step 7: Send confirmation to user in DM with button to return to chat
        try
        {
            var chatName = groupChat.Title ?? "the chat";

            // Build deep link to navigate to chat using extracted builder
            var chatDeepLink = WelcomeDeepLinkBuilder.BuildPublicChatLink(groupChat.Username);

            // For private chats (no username), try to get invite link
            if (chatDeepLink == null)
            {
                chatDeepLink = await chatService.GetInviteLinkAsync(groupChat.Id, cancellationToken);

                if (chatDeepLink != null)
                {
                    logger.LogDebug("Using invite link for private {Chat}: {Link}", groupChat.ToLogDebug(), chatDeepLink);
                }
                else
                {
                    logger.LogWarning("Could not get invite link for private {Chat}", groupChat.ToLogDebug());
                }
            }
            else
            {
                logger.LogDebug("Using public chat link for {Chat}: {Link}", groupChat.ToLogDebug(), chatDeepLink);
            }

            // Build keyboard and confirmation message using extracted builders
            InlineKeyboardMarkup? keyboard = null;
            if (chatDeepLink != null)
            {
                keyboard = WelcomeKeyboardBuilder.BuildReturnToChatKeyboard(chatName, chatDeepLink);

                logger.LogDebug(
                    "Sent confirmation with chat deep link to {User}: {DeepLink}",
                    user.ToLogDebug(),
                    chatDeepLink);
            }

            var confirmationText = WelcomeMessageBuilder.FormatDmAcceptanceConfirmation(chatName);
            await dmDeliveryService.SendDmAsync(user.Id, confirmationText, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send confirmation to {User}", user.ToLogDebug());
        }

        // Note: Timeout job will automatically skip when it sees response != "pending"
        // No need to explicitly cancel - Quartz.NET job checks database state first
    }

    private async Task<(bool DmSent, bool DmFallback)> SendRulesAsync(
        Chat chat,
        User user,
        WelcomeConfig config,
        CancellationToken cancellationToken = default)
    {
        var chatName = chat.Title ?? "this chat";
        var username = TelegramDisplayName.FormatMention(user);

        // Use extracted builder for rules confirmation message (includes footer)
        var dmText = WelcomeMessageBuilder.FormatRulesConfirmation(config, username, chatName);

        // Delegate to DmDeliveryService with chat fallback and 30-second auto-delete
        var result = await dmDeliveryService.SendDmAsync(
            telegramUserId: user.Id,
            messageText: dmText,
            fallbackChatId: chat.Id,
            autoDeleteSeconds: 30,
            cancellationToken: cancellationToken);

        logger.LogDebug(
            "Rules sent to {User}: DmSent={DmSent}, FallbackUsed={FallbackUsed}",
            user.ToLogDebug(),
            result.DmSent,
            result.FallbackUsed);

        return (DmSent: result.DmSent, DmFallback: result.FallbackUsed);
    }

    private async Task HandleUserLeftAsync(Chat chat, User user, CancellationToken cancellationToken = default)
    {
        logger.LogDebug(
            "{User} left {Chat}, recording welcome response if pending",
            user.ToLogDebug(),
            chat.ToLogDebug());

        try
        {
            // Cancel any active exam session
            if (await examFlowService.HasActiveSessionAsync(ChatIdentity.From(chat), UserIdentity.From(user), cancellationToken))
            {
                await examFlowService.CancelSessionAsync(ChatIdentity.From(chat), UserIdentity.From(user), cancellationToken);
                logger.LogDebug(
                    "Cancelled exam session for {User} who left {Chat}",
                    user.ToLogDebug(),
                    chat.ToLogDebug());
            }

            // Find any pending welcome response for this user
            var response = await welcomeResponsesRepository.GetByUserAndChatAsync(user.Id, chat.Id, cancellationToken);

            if (response == null || response.Response != WelcomeResponseType.Pending)
            {
                logger.LogDebug(
                    "No pending welcome response found for {User} in {Chat}",
                    user.ToLogDebug(),
                    chat.ToLogDebug());
                return;
            }

            // Cancel timeout job if it exists
            if (response.TimeoutJobId != null)
            {
                if (await jobScheduler.CancelJobAsync(response.TimeoutJobId, cancellationToken))
                {
                    await welcomeResponsesRepository.SetTimeoutJobIdAsync(response.Id, null, cancellationToken);
                }
            }

            // Mark as left
            await welcomeResponsesRepository.UpdateResponseAsync(response.Id, WelcomeResponseType.Left, dmSent: false, dmFallback: false, cancellationToken);

            logger.LogDebug(
                "Recorded welcome response 'left' for {User} in {Chat}",
                user.ToLogDebug(),
                chat.ToLogDebug());
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to handle user left for {User} in {Chat}",
                user.ToLogDebug(),
                chat.ToLogDebug());
        }
    }
}
