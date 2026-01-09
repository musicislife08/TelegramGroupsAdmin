using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.Core.JobPayloads;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Moderation;
using TelegramGroupsAdmin.Telegram.Services.Welcome;

namespace TelegramGroupsAdmin.Telegram.Services;

public class WelcomeService : IWelcomeService
{
    private readonly ILogger<WelcomeService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IBotProtectionService _botProtectionService;
    private readonly IDmDeliveryService _dmDeliveryService;
    private readonly IJobScheduler _jobScheduler;
    private readonly ITelegramBotClientFactory _botClientFactory;
    private readonly ICasCheckService _casCheckService;

    public WelcomeService(
        ILogger<WelcomeService> logger,
        IServiceProvider serviceProvider,
        IBotProtectionService botProtectionService,
        IDmDeliveryService dmDeliveryService,
        IJobScheduler jobScheduler,
        ITelegramBotClientFactory botClientFactory,
        ICasCheckService casCheckService)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _botProtectionService = botProtectionService;
        _dmDeliveryService = dmDeliveryService;
        _jobScheduler = jobScheduler;
        _botClientFactory = botClientFactory;
        _casCheckService = casCheckService;
    }

    private async Task<T> WithRepositoryAsync<T>(Func<IWelcomeResponsesRepository, CancellationToken, Task<T>> action, CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IWelcomeResponsesRepository>();
        return await action(repository, cancellationToken);
    }

    private async Task WithRepositoryAsync(Func<IWelcomeResponsesRepository, CancellationToken, Task> action, CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IWelcomeResponsesRepository>();
        await action(repository, cancellationToken);
    }


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
            var shouldAllow = await _botProtectionService.ShouldAllowBotAsync(
                chatMemberUpdate.Chat,
                user,
                chatMemberUpdate);

            if (!shouldAllow)
            {
                // Ban the bot
                await _botProtectionService.BanBotAsync(
                    chatMemberUpdate.Chat,
                    user,
                    "Not whitelisted and not invited by admin",
                    cancellationToken);
                return;
            }

            // Bot is allowed (whitelisted or admin-invited) - skip welcome message
            _logger.LogDebug("Skipping welcome for allowed bot {User}", user.ToLogDebug());
            return;
        }

        _logger.LogInformation(
            "New user joined: {User} in {Chat}",
            user.ToLogInfo(),
            chatMemberUpdate.Chat.ToLogInfo());

        // Load welcome config from database (chat-specific or global fallback)
        // Must create scope because WelcomeService is singleton but IConfigService is scoped
        using var scope = _serviceProvider.CreateScope();
        var configService = scope.ServiceProvider.GetRequiredService<IConfigService>();
        var config = await configService.GetEffectiveAsync<WelcomeConfig>(ConfigType.Welcome, chatMemberUpdate.Chat.Id)
                     ?? WelcomeConfig.Default;

        if (!config.Enabled)
        {
            _logger.LogDebug("Welcome system disabled for {Chat}", chatMemberUpdate.Chat.ToLogDebug());
            return;
        }

        try
        {
            var operations = await _botClientFactory.GetOperationsAsync();

            // Check if user is an admin/owner - skip welcome for admins
            var chatMember = await operations.GetChatMemberAsync(chatMemberUpdate.Chat.Id, user.Id, cancellationToken);
            if (chatMember.Status is ChatMemberStatus.Administrator or ChatMemberStatus.Creator)
            {
                _logger.LogInformation(
                    "Skipping welcome for admin/owner: {User} in {Chat}",
                    user.ToLogInfo(),
                    chatMemberUpdate.Chat.ToLogInfo());
                return;
            }

            // Step 1: FIRST - Restrict user permissions (mute immediately)
            // User can't do harm while we run checks
            await RestrictUserPermissionsAsync(operations, chatMemberUpdate.Chat, user, cancellationToken);

            // Create user record if not exists (IsActive: false - not engaged yet)
            // Must happen for ALL joining users, not just those checked for impersonation
            using var impersonationScope = _serviceProvider.CreateScope();
            var telegramUserRepo = impersonationScope.ServiceProvider.GetRequiredService<ITelegramUserRepository>();
            var existingUser = await telegramUserRepo.GetByTelegramIdAsync(user.Id, cancellationToken);

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
                await telegramUserRepo.UpsertAsync(newUser, cancellationToken);

                _logger.LogInformation(
                    "Created inactive user record for {User} on join",
                    user.ToLogInfo());
            }

            // Step 2: Check for impersonation (name + photo similarity vs admins)
            var impersonationService = impersonationScope.ServiceProvider.GetRequiredService<IImpersonationDetectionService>();
            var shouldCheck = await impersonationService.ShouldCheckUserAsync(user.Id, chatMemberUpdate.Chat.Id);

            if (shouldCheck)
            {
                _logger.LogDebug(
                    "Checking {User} for impersonation in {Chat}",
                    user.ToLogDebug(),
                    chatMemberUpdate.Chat.ToLogDebug());

                // Get user's photo path if available (may be null if not cached yet)
                var photoPath = existingUser?.UserPhotoPath;

                // Check for impersonation
                var impersonationResult = await impersonationService.CheckUserAsync(
                    user,
                    chatMemberUpdate.Chat,
                    photoPath);

                if (impersonationResult != null)
                {
                    _logger.LogWarning(
                        "Impersonation detected for {User} in {Chat} (score: {Score}, risk: {Risk})",
                        user.ToLogDebug(),
                        chatMemberUpdate.Chat.ToLogDebug(),
                        impersonationResult.TotalScore,
                        impersonationResult.RiskLevel);

                    // Execute action (create alert, auto-ban if score >= 100)
                    await impersonationService.ExecuteActionAsync(impersonationResult);

                    // If auto-banned (score 100), skip welcome flow
                    if (impersonationResult.ShouldAutoBan)
                    {
                        _logger.LogInformation(
                            "{User} auto-banned for impersonation, skipping welcome flow",
                            user.ToLogInfo());
                        return;
                    }

                    // Score 50-99: Continue with welcome flow (alert created for manual review)
                    _logger.LogInformation(
                        "{User} flagged for impersonation review (score: {Score}), continuing with welcome flow",
                        user.ToLogInfo(),
                        impersonationResult.TotalScore);
                }
            }

            // Step 3: CAS (Combot Anti-Spam) check - auto-ban known spammers
            var casResult = await _casCheckService.CheckUserAsync(user.Id, cancellationToken);
            if (casResult.IsBanned)
            {
                _logger.LogWarning(
                    "CAS banned user detected: {User} in {Chat} (reason: {Reason})",
                    user.ToLogInfo(),
                    chatMemberUpdate.Chat.ToLogInfo(),
                    casResult.Reason ?? "No reason provided");

                // Ban user using ModerationOrchestrator
                var moderationOrchestrator = impersonationScope.ServiceProvider.GetRequiredService<ModerationOrchestrator>();
                var reason = $"CAS banned: {casResult.Reason ?? "Listed in CAS database"}";
                await moderationOrchestrator.BanUserAsync(
                    userId: user.Id,
                    messageId: null,
                    executor: Core.Models.Actor.Cas,
                    reason: reason,
                    cancellationToken);

                _logger.LogInformation(
                    "{User} auto-banned (CAS), skipping welcome flow",
                    user.ToLogInfo());
                return;
            }

            // Step 4: Send welcome message with inline buttons
            var welcomeMessage = await SendWelcomeMessageAsync(
                operations,
                chatMemberUpdate.Chat.Id,
                user,
                config,
                cancellationToken);

            // Step 5: Create welcome response record (pending state)
            var welcomeResponse = new WelcomeResponse(
                Id: 0, // Will be set by database
                ChatId: chatMemberUpdate.Chat.Id,
                UserId: user.Id,
                Username: user.Username,
                WelcomeMessageId: welcomeMessage.MessageId,
                Response: WelcomeResponseType.Pending,
                RespondedAt: DateTimeOffset.UtcNow,
                DmSent: false,
                DmFallback: false,
                CreatedAt: DateTimeOffset.UtcNow,
                TimeoutJobId: null // Will be set after scheduling job
            );

            var responseId = await WithRepositoryAsync((repo, cancellationToken) => repo.InsertAsync(welcomeResponse, cancellationToken), cancellationToken);

            // Step 6: Schedule timeout via Quartz.NET (replaces fire-and-forget Task.Run)
            var payload = new WelcomeTimeoutPayload(
                chatMemberUpdate.Chat.Id,
                user.Id,
                welcomeMessage.MessageId
            );

            var jobId = await _jobScheduler.ScheduleJobAsync(
                "WelcomeTimeout",
                payload,
                delaySeconds: config.TimeoutSeconds,
                cancellationToken);

            // Store the job ID in the welcome response record
            await WithRepositoryAsync((repo, cancellationToken) => repo.SetTimeoutJobIdAsync(responseId, jobId, cancellationToken), cancellationToken);

            _logger.LogInformation(
                "Successfully scheduled welcome timeout for {User} in {Chat} (timeout: {Timeout}s, JobId: {JobId})",
                user.ToLogInfo(),
                chatMemberUpdate.Chat.ToLogInfo(),
                config.TimeoutSeconds,
                jobId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to process welcome for {User} in {Chat}",
                user.ToLogDebug(),
                chatMemberUpdate.Chat.ToLogDebug());
        }
    }

    public async Task HandleCallbackQueryAsync(
        CallbackQuery callbackQuery,
        CancellationToken cancellationToken)
    {
        var operations = await _botClientFactory.GetOperationsAsync();

        var data = callbackQuery.Data;
        var user = callbackQuery.From;
        var message = callbackQuery.Message;

        if (message == null || string.IsNullOrEmpty(data))
        {
            _logger.LogWarning("Callback query missing message or data");
            return;
        }

        var chatId = message.Chat.Id;

        _logger.LogInformation(
            "Callback query received: {Data} from {User} in {Chat}",
            data,
            user.ToLogInfo(),
            message.Chat.ToLogInfo());

        // Use extracted parser for callback data
        var parsedCallback = WelcomeCallbackParser.ParseCallbackData(data);
        if (parsedCallback == null)
        {
            _logger.LogWarning("Invalid or unrecognized callback data format: {Data}", data);
            return;
        }

        // Validate that the clicking user is the target user
        if (!WelcomeCallbackParser.ValidateCallerIsTarget(user.Id, parsedCallback.UserId))
        {
            _logger.LogWarning(
                "Wrong user clicked button: {Clicker} clicked button for target user {TargetUserId}",
                user.ToLogDebug(),
                parsedCallback.UserId);

            // For DM accept, just show alert (no message in DM chat)
            if (parsedCallback.Type == WelcomeCallbackType.DmAccept)
            {
                await operations.AnswerCallbackQueryAsync(
                    callbackQueryId: callbackQuery.Id,
                    text: "⚠️ This button is not for you.",
                    cancellationToken: cancellationToken);
                return;
            }

            // For chat buttons, send temporary warning message
            await SendWrongUserWarningAsync(operations, chatId, user, message.MessageId, cancellationToken);
            return;
        }

        // Route to appropriate handler based on callback type
        try
        {
            switch (parsedCallback.Type)
            {
                case WelcomeCallbackType.DmAccept:
                    await HandleDmAcceptAsync(
                        operations,
                        parsedCallback.ChatId!.Value,
                        user,
                        message.Chat.Id,
                        message.MessageId,
                        cancellationToken);
                    break;

                case WelcomeCallbackType.Accept:
                case WelcomeCallbackType.Deny:
                    // Load welcome config for chat-based callbacks
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var configService = scope.ServiceProvider.GetRequiredService<IConfigService>();
                        var config = await configService.GetEffectiveAsync<WelcomeConfig>(ConfigType.Welcome, chatId)
                                     ?? WelcomeConfig.Default;

                        if (parsedCallback.Type == WelcomeCallbackType.Accept)
                        {
                            await HandleAcceptAsync(operations, message.Chat, user, message.MessageId, config, cancellationToken);
                        }
                        else
                        {
                            await HandleDenyAsync(operations, message.Chat, user, message.MessageId, cancellationToken);
                        }
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to handle callback {Data} for {User} in {Chat}",
                data,
                user.ToLogDebug(),
                message.Chat.ToLogDebug());
        }
    }

    private async Task SendWrongUserWarningAsync(
        ITelegramOperations operations,
        long chatId,
        User user,
        int replyToMessageId,
        CancellationToken cancellationToken)
    {
        try
        {
            var username = TelegramDisplayName.FormatMention(user.FirstName, user.LastName, user.Username, user.Id);
            var warningText = WelcomeMessageBuilder.FormatWrongUserWarning(username);
            var warningMsg = await operations.SendMessageAsync(
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

            await _jobScheduler.ScheduleJobAsync(
                "DeleteMessage",
                deletePayload,
                delaySeconds: 10,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send warning message");
        }
    }

    private async Task<Message> SendWelcomeMessageAsync(
        ITelegramOperations operations,
        long chatId,
        User user,
        WelcomeConfig config,
        CancellationToken cancellationToken = default)
    {
        var username = TelegramDisplayName.FormatMention(user.FirstName, user.LastName, user.Username, user.Id);

        // Get chat name for variable substitution
        var chatInfo = await operations.GetChatAsync(chatId, cancellationToken);
        var chatName = chatInfo.Title ?? "this chat";

        // Use extracted builder for message formatting
        var messageText = WelcomeMessageBuilder.FormatWelcomeMessage(config, username, chatName);

        // Build keyboard based on welcome mode using extracted builders
        InlineKeyboardMarkup keyboard;

        if (config.Mode == WelcomeMode.DmWelcome)
        {
            var botInfo = await operations.GetMeAsync(cancellationToken);
            keyboard = WelcomeKeyboardBuilder.BuildDmModeKeyboard(config, chatId, user.Id, botInfo.Username!);

            _logger.LogInformation(
                "Sending DM welcome message to {User} in {Chat}",
                user.ToLogInfo(),
                chatInfo.ToLogInfo());
        }
        else
        {
            keyboard = WelcomeKeyboardBuilder.BuildChatModeKeyboard(config, user.Id);

            _logger.LogInformation(
                "Sending chat accept/deny welcome message to {User} in {Chat}",
                user.ToLogInfo(),
                chatInfo.ToLogInfo());
        }

        var message = await operations.SendMessageAsync(
            chatId: chatId,
            text: messageText,
            replyMarkup: keyboard,
            cancellationToken: cancellationToken);

        return message;
    }

    private async Task RestrictUserPermissionsAsync(
        ITelegramOperations operations,
        Chat chat,
        User user,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await operations.RestrictChatMemberAsync(
                chatId: chat.Id,
                userId: user.Id,
                permissions: WelcomeChatPermissions.Restricted,
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Restricted permissions for {User} in {Chat}",
                user.ToLogInfo(),
                chat.ToLogInfo());
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to restrict {User} in {Chat}",
                user.ToLogDebug(),
                chat.ToLogDebug());
            throw;
        }
    }

    private async Task RestoreUserPermissionsAsync(
        ITelegramOperations operations,
        Chat chat,
        User user,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Check if user is admin/owner - can't modify their permissions
            var chatMember = await operations.GetChatMemberAsync(chat.Id, user.Id, cancellationToken);
            if (chatMember.Status is ChatMemberStatus.Administrator or ChatMemberStatus.Creator)
            {
                _logger.LogDebug(
                    "Skipping permission restore for admin/owner: {User} in {Chat}",
                    user.ToLogDebug(),
                    chat.ToLogDebug());
                return;
            }

            // Get chat's default permissions to restore user to group defaults
            var chatDetails = await operations.GetChatAsync(chat.Id, cancellationToken);
            var defaultPermissions = chatDetails.Permissions ?? WelcomeChatPermissions.Default;

            _logger.LogDebug(
                "Restoring {User} to {Chat} default permissions: Messages={CanSendMessages}, Media={CanSendPhotos}",
                user.ToLogDebug(),
                chat.ToLogDebug(),
                defaultPermissions.CanSendMessages,
                defaultPermissions.CanSendPhotos);

            await operations.RestrictChatMemberAsync(
                chatId: chat.Id,
                userId: user.Id,
                permissions: defaultPermissions,
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Restored permissions for {User} in {Chat}",
                user.ToLogInfo(),
                chat.ToLogInfo());
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to restore permissions for {User} in {Chat}",
                user.ToLogDebug(),
                chat.ToLogDebug());
            throw;
        }
    }

    private async Task KickUserAsync(
        ITelegramOperations operations,
        Chat chat,
        User user,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Ban then immediately unban (removes user from chat without permanent ban)
            await operations.BanChatMemberAsync(chatId: chat.Id, userId: user.Id, cancellationToken: cancellationToken);
            await operations.UnbanChatMemberAsync(chatId: chat.Id, userId: user.Id, cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Kicked {User} from {Chat}",
                user.ToLogInfo(),
                chat.ToLogInfo());
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to kick {User} from {Chat}",
                user.ToLogDebug(),
                chat.ToLogDebug());
            throw;
        }
    }

    private async Task HandleAcceptAsync(
        ITelegramOperations operations,
        Chat chat,
        User user,
        int welcomeMessageId,
        WelcomeConfig config,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "{User} accepted rules in {Chat}",
            user.ToLogInfo(),
            chat.ToLogInfo());

        // Step 1: Check if user already responded (from pending record created on join)
        var existingResponse = await WithRepositoryAsync((repo, cancellationToken) => repo.GetByUserAndChatAsync(user.Id, chat.Id, cancellationToken), cancellationToken);

        // Step 2: Cancel timeout job if it exists
        if (existingResponse?.TimeoutJobId != null)
        {
            if (await _jobScheduler.CancelJobAsync(existingResponse.TimeoutJobId, cancellationToken))
            {
                // Clear the job ID since it's been cancelled
                await WithRepositoryAsync((repo, cancellationToken) => repo.SetTimeoutJobIdAsync(existingResponse.Id, null, cancellationToken), cancellationToken);
            }
        }

        // Step 3: Try to send rules via DM (or fallback to chat)
        // Always attempt this - previous DM sent via /start may have been deleted by user
        var (dmSent, dmFallback) = await SendRulesAsync(operations, chat, user, config, cancellationToken);

        _logger.LogInformation(
            "Rules delivery for {User}: DM sent: {DmSent}, Fallback: {DmFallback}",
            user.ToLogInfo(),
            dmSent,
            dmFallback);

        // Step 4: Restore user permissions
        await RestoreUserPermissionsAsync(operations, chat, user, cancellationToken);

        // Step 4b: Mark user as active (completed welcome flow)
        using (var scope = _serviceProvider.CreateScope())
        {
            var telegramUserRepo = scope.ServiceProvider.GetRequiredService<ITelegramUserRepository>();
            await telegramUserRepo.SetActiveAsync(user.Id, true, cancellationToken);
        }

        // Step 5: Delete welcome message
        try
        {
            await operations.DeleteMessageAsync(chatId: chat.Id, messageId: welcomeMessageId, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete welcome message {MessageId}", welcomeMessageId);
        }

        // Step 6: Update or create response record
        if (existingResponse != null)
        {
            // Update existing record
            await WithRepositoryAsync((repo, cancellationToken) => repo.UpdateResponseAsync(existingResponse.Id, WelcomeResponseType.Accepted, dmSent, dmFallback, cancellationToken), cancellationToken);
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
            await WithRepositoryAsync((repo, cancellationToken) => repo.InsertAsync(newResponse, cancellationToken), cancellationToken);
        }
    }

    private async Task HandleDenyAsync(
        ITelegramOperations operations,
        Chat chat,
        User user,
        int welcomeMessageId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "{User} denied rules in {Chat}",
            user.ToLogInfo(),
            chat.ToLogInfo());

        // Step 1: Cancel timeout job if it exists
        var existingResponse = await WithRepositoryAsync((repo, cancellationToken) => repo.GetByUserAndChatAsync(user.Id, chat.Id, cancellationToken), cancellationToken);
        if (existingResponse?.TimeoutJobId != null)
        {
            if (await _jobScheduler.CancelJobAsync(existingResponse.TimeoutJobId, cancellationToken))
            {
                await WithRepositoryAsync((repo, cancellationToken) => repo.SetTimeoutJobIdAsync(existingResponse.Id, null, cancellationToken), cancellationToken);
            }
        }

        // Step 2: Kick user
        await KickUserAsync(operations, chat, user, cancellationToken);

        // Step 3: Delete welcome message
        try
        {
            await operations.DeleteMessageAsync(chatId: chat.Id, messageId: welcomeMessageId, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete welcome message {MessageId}", welcomeMessageId);
        }

        // Step 4: Update or create response record
        if (existingResponse != null)
        {
            await WithRepositoryAsync((repo, cancellationToken) => repo.UpdateResponseAsync(existingResponse.Id, WelcomeResponseType.Denied, dmSent: false, dmFallback: false, cancellationToken), cancellationToken);
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
            await WithRepositoryAsync((repo, cancellationToken) => repo.InsertAsync(newResponse, cancellationToken), cancellationToken);
        }
    }

    private async Task HandleDmAcceptAsync(
        ITelegramOperations operations,
        long groupChatId,
        User user,
        long dmChatId,
        int buttonMessageId,
        CancellationToken cancellationToken = default)
    {
        // Fetch group chat info early for logging throughout the method
        var groupChat = await operations.GetChatAsync(groupChatId, cancellationToken);

        _logger.LogInformation(
            "{User} accepted rules via DM for {Chat}",
            user.ToLogInfo(),
            groupChat.ToLogInfo());

        // Step 1: Delete the Accept button message in DM (separate message from rules)
        try
        {
            await operations.DeleteMessageAsync(
                chatId: dmChatId,
                messageId: buttonMessageId,
                cancellationToken: cancellationToken);

            _logger.LogDebug(
                "Deleted DM Accept button message {MessageId} for {User}",
                buttonMessageId,
                user.ToLogDebug());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to delete DM Accept button message {MessageId}",
                buttonMessageId);
            // Non-fatal - continue with acceptance flow
        }

        // Step 2: Find the welcome response record
        var welcomeResponse = await WithRepositoryAsync((repo, cancellationToken) => repo.GetByUserAndChatAsync(user.Id, groupChatId, cancellationToken), cancellationToken);

        if (welcomeResponse == null)
        {
            _logger.LogWarning(
                "No welcome response found for {User} in {Chat}",
                user.ToLogDebug(),
                groupChat.ToLogDebug());

            // Send error to user in DM
            await operations.SendMessageAsync(
                chatId: user.Id,
                text: "❌ Could not find your welcome record. Please try accepting in the group chat instead.",
                cancellationToken: cancellationToken);
            return;
        }

        // Step 3: Cancel timeout job if it exists
        if (welcomeResponse.TimeoutJobId != null)
        {
            if (await _jobScheduler.CancelJobAsync(welcomeResponse.TimeoutJobId, cancellationToken))
            {
                await WithRepositoryAsync((repo, cancellationToken) => repo.SetTimeoutJobIdAsync(welcomeResponse.Id, null, cancellationToken), cancellationToken);
            }
        }

        // Step 4: Restore user permissions in group
        try
        {
            await RestoreUserPermissionsAsync(operations, groupChat, user, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to restore permissions for {User} in {Chat}",
                user.ToLogDebug(),
                groupChat.ToLogDebug());

            // Send error to user in DM
            await operations.SendMessageAsync(
                chatId: user.Id,
                text: "❌ Failed to restore your permissions. Please contact an admin.",
                cancellationToken: cancellationToken);
            return;
        }

        // Step 4b: Mark user as active (completed welcome flow via DM)
        using (var scope = _serviceProvider.CreateScope())
        {
            var telegramUserRepo = scope.ServiceProvider.GetRequiredService<ITelegramUserRepository>();
            await telegramUserRepo.SetActiveAsync(user.Id, true, cancellationToken);
        }

        // Step 5: Delete welcome message in group
        try
        {
            await operations.DeleteMessageAsync(
                chatId: groupChatId,
                messageId: welcomeResponse.WelcomeMessageId,
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Deleted welcome message {MessageId} in {Chat}",
                welcomeResponse.WelcomeMessageId,
                groupChat.ToLogInfo());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to delete welcome message {MessageId} in {Chat}",
                welcomeResponse.WelcomeMessageId,
                groupChat.ToLogDebug());
            // Non-fatal - continue with response update
        }

        // Step 6: Update welcome response record (mark as accepted via DM)
        await WithRepositoryAsync((repo, cancellationToken) => repo.UpdateResponseAsync(welcomeResponse.Id, WelcomeResponseType.Accepted, dmSent: true, dmFallback: false, cancellationToken), cancellationToken);

        // Step 7: Send confirmation to user in DM with button to return to chat
        try
        {
            var chatName = groupChat.Title ?? "the chat";

            // Build deep link to navigate to chat using extracted builder
            var chatDeepLink = WelcomeDeepLinkBuilder.BuildPublicChatLink(groupChat.Username);

            // For private chats (no username), try to get invite link
            if (chatDeepLink == null)
            {
                using var inviteLinkScope = _serviceProvider.CreateScope();
                var inviteLinkService = inviteLinkScope.ServiceProvider.GetRequiredService<IChatInviteLinkService>();
                chatDeepLink = await inviteLinkService.GetInviteLinkAsync(groupChat, cancellationToken);

                if (chatDeepLink != null)
                {
                    _logger.LogDebug("Using invite link for private {Chat}: {Link}", groupChat.ToLogDebug(), chatDeepLink);
                }
                else
                {
                    _logger.LogWarning("Could not get invite link for private {Chat}", groupChat.ToLogDebug());
                }
            }
            else
            {
                _logger.LogDebug("Using public chat link for {Chat}: {Link}", groupChat.ToLogDebug(), chatDeepLink);
            }

            // Build keyboard and confirmation message using extracted builders
            InlineKeyboardMarkup? keyboard = null;
            if (chatDeepLink != null)
            {
                keyboard = WelcomeKeyboardBuilder.BuildReturnToChatKeyboard(chatName, chatDeepLink);

                _logger.LogInformation(
                    "Sent confirmation with chat deep link to {User}: {DeepLink}",
                    user.ToLogInfo(),
                    chatDeepLink);
            }

            var confirmationText = WelcomeMessageBuilder.FormatDmAcceptanceConfirmation(chatName);
            await operations.SendMessageAsync(
                chatId: user.Id,
                text: confirmationText,
                replyMarkup: keyboard,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send confirmation to {User}", user.ToLogDebug());
        }

        // Note: Timeout job will automatically skip when it sees response != "pending"
        // No need to explicitly cancel - Quartz.NET job checks database state first
    }

    private async Task<(bool DmSent, bool DmFallback)> SendRulesAsync(
        ITelegramOperations operations,
        Chat chat,
        User user,
        WelcomeConfig config,
        CancellationToken cancellationToken = default)
    {
        var chatName = chat.Title ?? "this chat";
        var username = TelegramDisplayName.FormatMention(user.FirstName, user.LastName, user.Username, user.Id);

        // Use extracted builder for rules confirmation message (includes footer)
        var dmText = WelcomeMessageBuilder.FormatRulesConfirmation(config, username, chatName);

        // Delegate to DmDeliveryService with chat fallback and 30-second auto-delete
        var result = await _dmDeliveryService.SendDmAsync(
            telegramUserId: user.Id,
            messageText: dmText,
            fallbackChatId: chat.Id,
            autoDeleteSeconds: 30,
            cancellationToken: cancellationToken);

        _logger.LogInformation(
            "Rules sent to {User}: DmSent={DmSent}, FallbackUsed={FallbackUsed}",
            user.ToLogInfo(),
            result.DmSent,
            result.FallbackUsed);

        return (DmSent: result.DmSent, DmFallback: result.FallbackUsed);
    }

    private async Task HandleUserLeftAsync(Chat chat, User user, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "{User} left {Chat}, recording welcome response if pending",
            user.ToLogInfo(),
            chat.ToLogInfo());

        try
        {
            // Find any pending welcome response for this user
            var response = await WithRepositoryAsync((repo, cancellationToken) => repo.GetByUserAndChatAsync(user.Id, chat.Id, cancellationToken), cancellationToken);

            if (response == null || response.Response != WelcomeResponseType.Pending)
            {
                _logger.LogDebug(
                    "No pending welcome response found for {User} in {Chat}",
                    user.ToLogDebug(),
                    chat.ToLogDebug());
                return;
            }

            // Cancel timeout job if it exists
            if (response.TimeoutJobId != null)
            {
                if (await _jobScheduler.CancelJobAsync(response.TimeoutJobId, cancellationToken))
                {
                    await WithRepositoryAsync((repo, cancellationToken) => repo.SetTimeoutJobIdAsync(response.Id, null, cancellationToken), cancellationToken);
                }
            }

            // Mark as left
            await WithRepositoryAsync((repo, cancellationToken) => repo.UpdateResponseAsync(response.Id, WelcomeResponseType.Left, dmSent: false, dmFallback: false, cancellationToken), cancellationToken);

            _logger.LogInformation(
                "Recorded welcome response 'left' for {User} in {Chat}",
                user.ToLogInfo(),
                chat.ToLogInfo());
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to handle user left for {User} in {Chat}",
                user.ToLogDebug(),
                chat.ToLogDebug());
        }
    }
}
