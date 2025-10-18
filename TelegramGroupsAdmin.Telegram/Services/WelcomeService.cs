using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TickerQ.Utilities;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models.Ticker;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Telegram.Abstractions.Jobs;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.Telegram.Services;

public interface IWelcomeService
{
    Task HandleChatMemberUpdateAsync(ITelegramBotClient botClient, ChatMemberUpdated chatMemberUpdate, CancellationToken cancellationToken);
    Task HandleCallbackQueryAsync(ITelegramBotClient botClient, CallbackQuery callbackQuery, CancellationToken cancellationToken);
}

public class WelcomeService : IWelcomeService
{
    private readonly ILogger<WelcomeService> _logger;
    private readonly TelegramOptions _telegramOptions;
    private readonly IServiceProvider _serviceProvider;
    private readonly IBotProtectionService _botProtectionService;

    public WelcomeService(
        ILogger<WelcomeService> logger,
        IOptions<TelegramOptions> telegramOptions,
        IServiceProvider serviceProvider,
        IBotProtectionService botProtectionService)
    {
        _logger = logger;
        _telegramOptions = telegramOptions.Value;
        _serviceProvider = serviceProvider;
        _botProtectionService = botProtectionService;
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

    /// <summary>
    /// Creates restricted permissions (all false) for new users awaiting welcome acceptance
    /// </summary>
    private static ChatPermissions CreateRestrictedPermissions() => new()
    {
        CanSendMessages = false,
        CanSendAudios = false,
        CanSendDocuments = false,
        CanSendPhotos = false,
        CanSendVideos = false,
        CanSendVideoNotes = false,
        CanSendVoiceNotes = false,
        CanSendPolls = false,
        CanSendOtherMessages = false,
        CanAddWebPagePreviews = false,
        CanChangeInfo = false,
        CanInviteUsers = false,
        CanPinMessages = false,
        CanManageTopics = false
    };

    /// <summary>
    /// Creates default permissions for accepted users (messaging enabled, admin features restricted)
    /// </summary>
    private static ChatPermissions CreateDefaultPermissions() => new()
    {
        CanSendMessages = true,
        CanSendAudios = true,
        CanSendDocuments = true,
        CanSendPhotos = true,
        CanSendVideos = true,
        CanSendVideoNotes = true,
        CanSendVoiceNotes = true,
        CanSendPolls = true,
        CanSendOtherMessages = true,
        CanAddWebPagePreviews = true,
        CanChangeInfo = false,      // Admin feature - restricted
        CanInviteUsers = true,
        CanPinMessages = false,     // Admin feature - restricted
        CanManageTopics = false     // Admin feature - restricted
    };

    public async Task HandleChatMemberUpdateAsync(
        ITelegramBotClient botClient,
        ChatMemberUpdated chatMemberUpdate,
        CancellationToken cancellationToken)
    {
        // Detect new user joins (status changed to Member)
        var oldStatus = chatMemberUpdate.OldChatMember.Status;
        var newStatus = chatMemberUpdate.NewChatMember.Status;
        var user = chatMemberUpdate.NewChatMember.User;

        // Handle user leaving (Member/Restricted → Left)
        if ((oldStatus == ChatMemberStatus.Member || oldStatus == ChatMemberStatus.Restricted) &&
            (newStatus == ChatMemberStatus.Left || newStatus == ChatMemberStatus.Kicked))
        {
            await HandleUserLeftAsync(chatMemberUpdate.Chat.Id, user.Id, cancellationToken);
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
                chatMemberUpdate.Chat.Id,
                user,
                chatMemberUpdate);

            if (!shouldAllow)
            {
                // Ban the bot
                await _botProtectionService.BanBotAsync(
                    botClient,
                    chatMemberUpdate.Chat.Id,
                    user,
                    "Not whitelisted and not invited by admin",
                    cancellationToken);
                return;
            }

            // Bot is allowed (whitelisted or admin-invited) - skip welcome message
            _logger.LogDebug("Skipping welcome for allowed bot user {UserId}", user.Id);
            return;
        }

        _logger.LogInformation(
            "New user joined: {UserId} (@{Username}) in chat {ChatId}",
            user.Id,
            user.Username,
            chatMemberUpdate.Chat.Id);

        // Load welcome config from database (chat-specific or global fallback)
        // Must create scope because WelcomeService is singleton but IConfigService is scoped
        using var scope = _serviceProvider.CreateScope();
        var configService = scope.ServiceProvider.GetRequiredService<IConfigService>();
        var config = await configService.GetEffectiveAsync<WelcomeConfig>(ConfigType.Welcome, chatMemberUpdate.Chat.Id)
                     ?? WelcomeConfig.Default;

        if (!config.Enabled)
        {
            _logger.LogDebug("Welcome system disabled for chat {ChatId}", chatMemberUpdate.Chat.Id);
            return;
        }

        try
        {
            // Check if user is an admin/owner - skip welcome for admins
            var chatMember = await botClient.GetChatMember(chatMemberUpdate.Chat.Id, user.Id, cancellationToken);
            if (chatMember.Status == ChatMemberStatus.Administrator || chatMember.Status == ChatMemberStatus.Creator)
            {
                _logger.LogInformation(
                    "Skipping welcome for admin/owner: User {UserId} (@{Username}) in chat {ChatId}",
                    user.Id,
                    user.Username,
                    chatMemberUpdate.Chat.Id);
                return;
            }

            // Phase 4.10: Check for impersonation (name + photo similarity vs admins)
            using (var impersonationScope = _serviceProvider.CreateScope())
            {
                var impersonationService = impersonationScope.ServiceProvider.GetRequiredService<IImpersonationDetectionService>();
                var telegramUserRepo = impersonationScope.ServiceProvider.GetRequiredService<TelegramUserRepository>();

                // Check if user should be screened for impersonation
                var shouldCheck = await impersonationService.ShouldCheckUserAsync(user.Id, chatMemberUpdate.Chat.Id);

                if (shouldCheck)
                {
                    _logger.LogDebug(
                        "Checking user {UserId} for impersonation in chat {ChatId}",
                        user.Id,
                        chatMemberUpdate.Chat.Id);

                    // Get user's photo path if available (may be null if not cached yet)
                    var existingUser = await telegramUserRepo.GetByTelegramIdAsync(user.Id, cancellationToken);
                    var photoPath = existingUser?.UserPhotoPath;

                    // Check for impersonation
                    var impersonationResult = await impersonationService.CheckUserAsync(
                        user.Id,
                        chatMemberUpdate.Chat.Id,
                        user.FirstName,
                        user.LastName,
                        photoPath);

                    if (impersonationResult != null)
                    {
                        _logger.LogWarning(
                            "Impersonation detected for user {UserId} in chat {ChatId} (score: {Score}, risk: {Risk})",
                            user.Id,
                            chatMemberUpdate.Chat.Id,
                            impersonationResult.TotalScore,
                            impersonationResult.RiskLevel);

                        // Execute action (create alert, auto-ban if score >= 100)
                        await impersonationService.ExecuteActionAsync(impersonationResult);

                        // If auto-banned (score 100), skip welcome flow
                        if (impersonationResult.ShouldAutoBan)
                        {
                            _logger.LogInformation(
                                "User {UserId} auto-banned for impersonation, skipping welcome flow",
                                user.Id);
                            return;
                        }

                        // Score 50-99: Continue with welcome flow (alert created for manual review)
                        _logger.LogInformation(
                            "User {UserId} flagged for impersonation review (score: {Score}), continuing with welcome flow",
                            user.Id,
                            impersonationResult.TotalScore);
                    }
                }
            }

            // Step 1: Restrict user permissions (mute on join)
            await RestrictUserPermissionsAsync(botClient, chatMemberUpdate.Chat.Id, user.Id, cancellationToken);

            // Step 2: Send welcome message with inline buttons
            var welcomeMessage = await SendWelcomeMessageAsync(
                botClient,
                chatMemberUpdate.Chat.Id,
                user,
                config,
                cancellationToken);

            // Step 3: Create welcome response record (pending state)
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

            var responseId = await WithRepositoryAsync((repo, ct) => repo.InsertAsync(welcomeResponse, ct), cancellationToken);

            // Step 4: Schedule timeout via TickerQ (replaces fire-and-forget Task.Run)
            var payload = new WelcomeTimeoutPayload(
                chatMemberUpdate.Chat.Id,
                user.Id,
                welcomeMessage.MessageId
            );

            try
            {
                _logger.LogDebug(
                    "Attempting to schedule TickerQ job for user {UserId} in chat {ChatId}",
                    user.Id,
                    chatMemberUpdate.Chat.Id);

                // Get TickerQ manager from scope (it's scoped, we're singleton)
                using var tickerScope = _serviceProvider.CreateScope();
                var timeTickerManager = tickerScope.ServiceProvider.GetRequiredService<ITimeTickerManager<TimeTicker>>();

                var executionTime = DateTime.UtcNow.AddSeconds(config.TimeoutSeconds);
                var request = TickerHelper.CreateTickerRequest(payload);

                _logger.LogDebug(
                    "TickerQ job details - Function: WelcomeTimeout, ExecutionTime: {ExecutionTime}, Payload: ChatId={ChatId}, UserId={UserId}, MessageId={MessageId}",
                    executionTime,
                    payload.ChatId,
                    payload.UserId,
                    payload.WelcomeMessageId);

                var result = await timeTickerManager.AddAsync(new TimeTicker
                {
                    Function = "WelcomeTimeout",
                    ExecutionTime = executionTime,
                    Request = request,
                    Retries = 1,
                    RetryIntervals = [30] // Retry once after 30s if it fails
                });

                if (!result.IsSucceded)
                {
                    throw result.Exception ?? new InvalidOperationException("TickerQ AddAsync failed without exception");
                }

                // Store the job ID in the welcome response record
                await WithRepositoryAsync((repo, ct) => repo.SetTimeoutJobIdAsync(responseId, result.Result?.Id, ct), cancellationToken);

                _logger.LogInformation(
                    "Successfully scheduled welcome timeout for user {UserId} in chat {ChatId} (timeout: {Timeout}s, JobId: {JobId})",
                    user.Id,
                    chatMemberUpdate.Chat.Id,
                    config.TimeoutSeconds,
                    result.Result?.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to schedule TickerQ timeout job for user {UserId} in chat {ChatId}",
                    user.Id,
                    chatMemberUpdate.Chat.Id);
                // Don't throw - we want to continue even if scheduling fails
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to process welcome for user {UserId} in chat {ChatId}",
                user.Id,
                chatMemberUpdate.Chat.Id);
        }
    }

    public async Task HandleCallbackQueryAsync(
        ITelegramBotClient botClient,
        CallbackQuery callbackQuery,
        CancellationToken cancellationToken)
    {
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
            "Callback query received: {Data} from user {UserId} in chat {ChatId}",
            data,
            user.Id,
            chatId);

        // Parse callback data
        // Format: "welcome_accept:123456", "welcome_deny:123456", or "dm_accept:chatId:userId"
        var parts = data.Split(':');
        var action = parts[0];

        // Handle dm_accept separately (3-part format: dm_accept:groupChatId:userId)
        if (action == "dm_accept")
        {
            if (parts.Length != 3 || !long.TryParse(parts[1], out var groupChatId) || !long.TryParse(parts[2], out var targetUserId))
            {
                _logger.LogWarning("Invalid dm_accept callback data format: {Data}", data);
                return;
            }

            // Validate that the clicking user is the target user
            if (user.Id != targetUserId)
            {
                _logger.LogWarning(
                    "Wrong user clicked DM accept button: User {ClickerId} clicked button for user {TargetUserId}",
                    user.Id,
                    targetUserId);
                await botClient.AnswerCallbackQuery(
                    callbackQueryId: callbackQuery.Id,
                    text: "⚠️ This button is not for you.",
                    showAlert: true,
                    cancellationToken: cancellationToken);
                return;
            }

            try
            {
                await HandleDmAcceptAsync(botClient, groupChatId, user, message.Chat.Id, message.MessageId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to handle dm_accept for user {UserId} in group {ChatId}",
                    user.Id,
                    groupChatId);
            }
            return;
        }

        // Handle welcome_accept and welcome_deny (2-part format)
        if (parts.Length != 2 || !long.TryParse(parts[1], out var targetUserIdForGroup))
        {
            _logger.LogWarning("Invalid callback data format: {Data}", data);
            return;
        }

        // Validate that the clicking user is the target user
        if (user.Id != targetUserIdForGroup)
        {
            _logger.LogWarning(
                "Wrong user clicked button: User {ClickerId} clicked button for user {TargetUserId}",
                user.Id,
                targetUserIdForGroup);

            // Send temporary warning message tagged to the wrong user
            try
            {
                var username = user.Username != null ? $"@{user.Username}" : user.FirstName;
                var warningMsg = await botClient.SendMessage(
                    chatId: chatId,
                    text: $"{username}, ⚠️ this button is not for you. Only the mentioned user can respond.",
                    replyParameters: new ReplyParameters { MessageId = message.MessageId },
                    cancellationToken: cancellationToken);

                // Delete warning after 10 seconds via TickerQ
                var deletePayload = new DeleteMessagePayload(
                    chatId,
                    warningMsg.MessageId,
                    "wrong_user_warning"
                );

                try
                {
                    _logger.LogDebug(
                        "Scheduling delete for warning message {MessageId} in chat {ChatId}",
                        warningMsg.MessageId,
                        chatId);

                    using var tickerScope2 = _serviceProvider.CreateScope();
                    var timeTickerManager2 = tickerScope2.ServiceProvider.GetRequiredService<ITimeTickerManager<TimeTicker>>();
                    var deleteResult = await timeTickerManager2.AddAsync(new TimeTicker
                    {
                        Function = "DeleteMessage",
                        ExecutionTime = DateTime.UtcNow.AddSeconds(10),
                        Request = TickerHelper.CreateTickerRequest(deletePayload),
                        Retries = 0 // Don't retry - message may have been manually deleted
                    });

                    if (!deleteResult.IsSucceded)
                    {
                        throw deleteResult.Exception ?? new InvalidOperationException("TickerQ AddAsync failed for delete job");
                    }

                    _logger.LogDebug(
                        "Successfully scheduled delete for warning message {MessageId} (JobId: {JobId})",
                        warningMsg.MessageId,
                        deleteResult.Result?.Id);
                }
                catch (Exception deleteEx)
                {
                    _logger.LogError(
                        deleteEx,
                        "Failed to schedule delete for warning message {MessageId} in chat {ChatId}",
                        warningMsg.MessageId,
                        chatId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send warning message");
            }

            return;
        }

        // Load welcome config from database (chat-specific or global fallback)
        // Must create scope because WelcomeService is singleton but IConfigService is scoped
        using var scope = _serviceProvider.CreateScope();
        var configService = scope.ServiceProvider.GetRequiredService<IConfigService>();
        var config = await configService.GetEffectiveAsync<WelcomeConfig>(ConfigType.Welcome, chatId)
                     ?? WelcomeConfig.Default;

        try
        {
            if (action == "welcome_accept")
            {
                await HandleAcceptAsync(botClient, chatId, user, message.MessageId, config, cancellationToken);
            }
            else if (action == "welcome_deny")
            {
                await HandleDenyAsync(botClient, chatId, user, message.MessageId, cancellationToken);
            }
            else
            {
                _logger.LogWarning("Unknown callback action: {Action}", action);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to handle callback {Data} for user {UserId} in chat {ChatId}",
                data,
                user.Id,
                chatId);
        }
    }

    private async Task<Message> SendWelcomeMessageAsync(
        ITelegramBotClient botClient,
        long chatId,
        User user,
        WelcomeConfig config,
        CancellationToken cancellationToken = default)
    {
        var username = user.Username != null ? $"@{user.Username}" : user.FirstName;
        var messageText = config.ChatWelcomeTemplate.Replace("{username}", username);

        // Add rules to chat message ONLY in ChatAcceptDeny mode
        if (config.Mode == WelcomeMode.ChatAcceptDeny)
        {
            messageText += $"\n\n📜 **Rules:**\n{config.RulesText}";
        }

        // Build keyboard based on welcome mode
        InlineKeyboardMarkup keyboard;

        if (config.Mode == WelcomeMode.DmWelcome)
        {
            // DM Welcome mode: Single row with deep link button
            var botInfo = await botClient.GetMe(cancellationToken);
            var deepLink = $"https://t.me/{botInfo.Username}?start=welcome_{chatId}_{user.Id}";

            keyboard = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithUrl("📖 Read Rules (Opens Bot Chat)", deepLink)
                }
            });

            _logger.LogInformation(
                "Sent DM welcome message {MessageId} to user {UserId} in chat {ChatId} with deep link: {DeepLink}",
                0, // Will be set after send
                user.Id,
                chatId,
                deepLink);
        }
        else // ChatAcceptDeny
        {
            // Chat Accept/Deny mode: Single row with Accept/Deny buttons
            keyboard = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData(config.AcceptButtonText, $"welcome_accept:{user.Id}"),
                    InlineKeyboardButton.WithCallbackData(config.DenyButtonText, $"welcome_deny:{user.Id}")
                }
            });

            _logger.LogInformation(
                "Sent chat accept/deny welcome message to user {UserId} in chat {ChatId}",
                user.Id,
                chatId);
        }

        var message = await botClient.SendMessage(
            chatId: chatId,
            text: messageText,
            replyMarkup: keyboard,
            cancellationToken: cancellationToken);

        return message;
    }

    private async Task RestrictUserPermissionsAsync(
        ITelegramBotClient botClient,
        long chatId,
        long userId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await botClient.RestrictChatMember(
                chatId: chatId,
                userId: userId,
                permissions: CreateRestrictedPermissions(),
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Restricted permissions for user {UserId} in chat {ChatId}",
                userId,
                chatId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to restrict user {UserId} in chat {ChatId}",
                userId,
                chatId);
            throw;
        }
    }

    private async Task RestoreUserPermissionsAsync(
        ITelegramBotClient botClient,
        long chatId,
        long userId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Check if user is admin/owner - can't modify their permissions
            var chatMember = await botClient.GetChatMember(chatId, userId, cancellationToken);
            if (chatMember.Status == ChatMemberStatus.Administrator || chatMember.Status == ChatMemberStatus.Creator)
            {
                _logger.LogDebug(
                    "Skipping permission restore for admin/owner: User {UserId} in chat {ChatId}",
                    userId,
                    chatId);
                return;
            }

            await botClient.RestrictChatMember(
                chatId: chatId,
                userId: userId,
                permissions: CreateDefaultPermissions(),
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Restored permissions for user {UserId} in chat {ChatId}",
                userId,
                chatId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to restore permissions for user {UserId} in chat {ChatId}",
                userId,
                chatId);
            throw;
        }
    }

    private async Task KickUserAsync(
        ITelegramBotClient botClient,
        long chatId,
        long userId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Ban then immediately unban (removes user from chat without permanent ban)
            await botClient.BanChatMember(chatId: chatId, userId: userId, cancellationToken: cancellationToken);
            await botClient.UnbanChatMember(chatId: chatId, userId: userId, cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Kicked user {UserId} from chat {ChatId}",
                userId,
                chatId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to kick user {UserId} from chat {ChatId}",
                userId,
                chatId);
            throw;
        }
    }

    private async Task HandleAcceptAsync(
        ITelegramBotClient botClient,
        long chatId,
        User user,
        int welcomeMessageId,
        WelcomeConfig config,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "User {UserId} (@{Username}) accepted rules in chat {ChatId}",
            user.Id,
            user.Username,
            chatId);

        // Step 1: Check if user already responded (from pending record created on join)
        var existingResponse = await WithRepositoryAsync((repo, ct) => repo.GetByUserAndChatAsync(user.Id, chatId, ct), cancellationToken);

        // Step 2: Cancel timeout job if it exists
        if (existingResponse?.TimeoutJobId.HasValue == true)
        {
            try
            {
                using var tickerScope = _serviceProvider.CreateScope();
                var timeTickerManager = tickerScope.ServiceProvider.GetRequiredService<ITimeTickerManager<TimeTicker>>();

                await timeTickerManager.DeleteAsync(existingResponse.TimeoutJobId.Value);

                _logger.LogInformation(
                    "Cancelled timeout job {JobId} for user {UserId} in chat {ChatId}",
                    existingResponse.TimeoutJobId.Value,
                    user.Id,
                    chatId);

                // Clear the job ID since it's been cancelled
                await WithRepositoryAsync((repo, ct) => repo.SetTimeoutJobIdAsync(existingResponse.Id, null, ct), cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to cancel timeout job {JobId} for user {UserId}",
                    existingResponse.TimeoutJobId.Value,
                    user.Id);
                // Non-fatal - continue with acceptance flow
            }
        }

        // Step 3: Try to send rules via DM (or fallback to chat)
        // Always attempt this - previous DM sent via /start may have been deleted by user
        var (dmSent, dmFallback) = await SendRulesAsync(botClient, chatId, user, config, cancellationToken);

        _logger.LogInformation(
            "Rules delivery for user {UserId}: DM sent: {DmSent}, Fallback: {DmFallback}",
            user.Id,
            dmSent,
            dmFallback);

        // Step 4: Restore user permissions
        await RestoreUserPermissionsAsync(botClient, chatId, user.Id, cancellationToken);

        // Step 5: Delete welcome message
        try
        {
            await botClient.DeleteMessage(chatId: chatId, messageId: welcomeMessageId, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete welcome message {MessageId}", welcomeMessageId);
        }

        // Step 6: Update or create response record
        if (existingResponse != null)
        {
            // Update existing record
            await WithRepositoryAsync((repo, ct) => repo.UpdateResponseAsync(existingResponse.Id, WelcomeResponseType.Accepted, dmSent, dmFallback, ct), cancellationToken);
        }
        else
        {
            // Create new record (shouldn't happen normally, but handle it)
            var newResponse = new WelcomeResponse(
                Id: 0,
                ChatId: chatId,
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
            await WithRepositoryAsync((repo, ct) => repo.InsertAsync(newResponse, ct), cancellationToken);
        }
    }

    private async Task HandleDenyAsync(
        ITelegramBotClient botClient,
        long chatId,
        User user,
        int welcomeMessageId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "User {UserId} (@{Username}) denied rules in chat {ChatId}",
            user.Id,
            user.Username,
            chatId);

        // Step 1: Cancel timeout job if it exists
        var existingResponse = await WithRepositoryAsync((repo, ct) => repo.GetByUserAndChatAsync(user.Id, chatId, ct), cancellationToken);
        if (existingResponse?.TimeoutJobId.HasValue == true)
        {
            try
            {
                using var tickerScope = _serviceProvider.CreateScope();
                var timeTickerManager = tickerScope.ServiceProvider.GetRequiredService<ITimeTickerManager<TimeTicker>>();

                await timeTickerManager.DeleteAsync(existingResponse.TimeoutJobId.Value);

                _logger.LogInformation(
                    "Cancelled timeout job {JobId} for user {UserId} in chat {ChatId}",
                    existingResponse.TimeoutJobId.Value,
                    user.Id,
                    chatId);

                await WithRepositoryAsync((repo, ct) => repo.SetTimeoutJobIdAsync(existingResponse.Id, null, ct), cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to cancel timeout job {JobId} for user {UserId}",
                    existingResponse.TimeoutJobId.Value,
                    user.Id);
                // Non-fatal - continue with deny flow
            }
        }

        // Step 2: Kick user
        await KickUserAsync(botClient, chatId, user.Id, cancellationToken);

        // Step 3: Delete welcome message
        try
        {
            await botClient.DeleteMessage(chatId: chatId, messageId: welcomeMessageId, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete welcome message {MessageId}", welcomeMessageId);
        }

        // Step 4: Update or create response record
        if (existingResponse != null)
        {
            await WithRepositoryAsync((repo, ct) => repo.UpdateResponseAsync(existingResponse.Id, WelcomeResponseType.Denied, dmSent: false, dmFallback: false, ct), cancellationToken);
        }
        else
        {
            var newResponse = new WelcomeResponse(
                Id: 0,
                ChatId: chatId,
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
            await WithRepositoryAsync((repo, ct) => repo.InsertAsync(newResponse, ct), cancellationToken);
        }
    }

    private async Task HandleDmAcceptAsync(
        ITelegramBotClient botClient,
        long groupChatId,
        User user,
        long dmChatId,
        int buttonMessageId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "User {UserId} (@{Username}) accepted rules via DM for chat {ChatId}",
            user.Id,
            user.Username,
            groupChatId);

        // Step 1: Delete the Accept button message in DM (separate message from rules)
        try
        {
            await botClient.DeleteMessage(
                chatId: dmChatId,
                messageId: buttonMessageId,
                cancellationToken: cancellationToken);

            _logger.LogDebug(
                "Deleted DM Accept button message {MessageId} for user {UserId}",
                buttonMessageId,
                user.Id);
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
        var welcomeResponse = await WithRepositoryAsync((repo, ct) => repo.GetByUserAndChatAsync(user.Id, groupChatId, ct), cancellationToken);

        if (welcomeResponse == null)
        {
            _logger.LogWarning(
                "No welcome response found for user {UserId} in chat {ChatId}",
                user.Id,
                groupChatId);

            // Send error to user in DM
            await botClient.SendMessage(
                chatId: user.Id,
                text: "❌ Could not find your welcome record. Please try accepting in the group chat instead.",
                cancellationToken: cancellationToken);
            return;
        }

        // Step 3: Cancel timeout job if it exists
        if (welcomeResponse.TimeoutJobId.HasValue)
        {
            try
            {
                using var tickerScope = _serviceProvider.CreateScope();
                var timeTickerManager = tickerScope.ServiceProvider.GetRequiredService<ITimeTickerManager<TimeTicker>>();

                await timeTickerManager.DeleteAsync(welcomeResponse.TimeoutJobId.Value);

                _logger.LogInformation(
                    "Cancelled timeout job {JobId} for user {UserId} in chat {ChatId}",
                    welcomeResponse.TimeoutJobId.Value,
                    user.Id,
                    groupChatId);

                await WithRepositoryAsync((repo, ct) => repo.SetTimeoutJobIdAsync(welcomeResponse.Id, null, ct), cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to cancel timeout job {JobId} for user {UserId}",
                    welcomeResponse.TimeoutJobId.Value,
                    user.Id);
                // Non-fatal - continue with acceptance flow
            }
        }

        // Step 4: Restore user permissions in group
        try
        {
            await RestoreUserPermissionsAsync(botClient, groupChatId, user.Id, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to restore permissions for user {UserId} in chat {ChatId}",
                user.Id,
                groupChatId);

            // Send error to user in DM
            await botClient.SendMessage(
                chatId: user.Id,
                text: "❌ Failed to restore your permissions. Please contact an admin.",
                cancellationToken: cancellationToken);
            return;
        }

        // Step 5: Delete welcome message in group
        try
        {
            await botClient.DeleteMessage(
                chatId: groupChatId,
                messageId: welcomeResponse.WelcomeMessageId,
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Deleted welcome message {MessageId} in chat {ChatId}",
                welcomeResponse.WelcomeMessageId,
                groupChatId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to delete welcome message {MessageId} in chat {ChatId}",
                welcomeResponse.WelcomeMessageId,
                groupChatId);
            // Non-fatal - continue with response update
        }

        // Step 6: Update welcome response record (mark as accepted via DM)
        await WithRepositoryAsync((repo, ct) => repo.UpdateResponseAsync(welcomeResponse.Id, WelcomeResponseType.Accepted, dmSent: true, dmFallback: false, ct), cancellationToken);

        // Step 6: Send confirmation to user in DM
        try
        {
            var chat = await botClient.GetChat(groupChatId, cancellationToken);
            var chatName = chat.Title ?? "the chat";

            await botClient.SendMessage(
                chatId: user.Id,
                text: $"✅ Welcome! You can now participate in {chatName}.",
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send confirmation to user {UserId}", user.Id);
        }

        // Note: Timeout job will automatically skip when it sees response != "pending"
        // No need to explicitly cancel - TickerQ job checks database state first
    }

    private async Task<string> GetChatNameAsync(ITelegramBotClient botClient, long chatId, CancellationToken cancellationToken = default)
    {
        try
        {
            var chat = await botClient.GetChat(chatId, cancellationToken);
            return chat.Title ?? "this chat";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get chat name for chat {ChatId}", chatId);
            return "this chat";
        }
    }

    private async Task<(bool DmSent, bool DmFallback)> SendRulesAsync(
        ITelegramBotClient botClient,
        long chatId,
        User user,
        WelcomeConfig config,
        CancellationToken cancellationToken = default)
    {
        var chatName = await GetChatNameAsync(botClient, chatId, cancellationToken);

        // Send rules without button instructions (user already accepted in group)
        // Just show the rules text, no action needed
        var dmText = $"Welcome to {chatName}! Here are our rules:\n\n{config.RulesText}\n\n✅ You're all set! You can now participate in the chat.";

        try
        {
            // Try to send DM
            await botClient.SendMessage(
                chatId: user.Id,
                text: dmText,
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Sent rules DM to user {UserId} (@{Username})",
                user.Id,
                user.Username);

            return (DmSent: true, DmFallback: false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to send rules DM to user {UserId}, falling back to chat message",
                user.Id);

            // Fallback: Send rules in chat with auto-delete after 30 seconds
            try
            {
                var fallbackText = config.ChatFallbackTemplate.Replace("{rules_text}", config.RulesText);
                var username = user.Username != null ? $"@{user.Username}" : user.FirstName;
                var messageText = $"{username}, {fallbackText}";

                var fallbackMessage = await botClient.SendMessage(
                    chatId: chatId,
                    text: messageText,
                    cancellationToken: cancellationToken);

                _logger.LogInformation(
                    "Sent fallback rules in chat {ChatId} for user {UserId}, will delete in 30 seconds",
                    chatId,
                    user.Id);

                // Auto-delete fallback message after 30 seconds via TickerQ
                var fallbackDeletePayload = new DeleteMessagePayload(
                    chatId,
                    fallbackMessage.MessageId,
                    "fallback_rules"
                );

                try
                {
                    _logger.LogDebug(
                        "Scheduling delete for fallback message {MessageId} in chat {ChatId}",
                        fallbackMessage.MessageId,
                        chatId);

                    using var tickerScope3 = _serviceProvider.CreateScope();
                    var timeTickerManager3 = tickerScope3.ServiceProvider.GetRequiredService<ITimeTickerManager<TimeTicker>>();
                    var fallbackDeleteResult = await timeTickerManager3.AddAsync(new TimeTicker
                    {
                        Function = "DeleteMessage",
                        ExecutionTime = DateTime.UtcNow.AddSeconds(30),
                        Request = TickerHelper.CreateTickerRequest(fallbackDeletePayload),
                        Retries = 0 // Don't retry - message may have been manually deleted
                    });

                    if (!fallbackDeleteResult.IsSucceded)
                    {
                        throw fallbackDeleteResult.Exception ?? new InvalidOperationException("TickerQ AddAsync failed for fallback delete job");
                    }

                    _logger.LogDebug(
                        "Successfully scheduled delete for fallback message {MessageId} (JobId: {JobId})",
                        fallbackMessage.MessageId,
                        fallbackDeleteResult.Result?.Id);
                }
                catch (Exception deleteEx)
                {
                    _logger.LogError(
                        deleteEx,
                        "Failed to schedule delete for fallback message {MessageId} in chat {ChatId}",
                        fallbackMessage.MessageId,
                        chatId);
                }

                return (DmSent: false, DmFallback: true);
            }
            catch (Exception fallbackEx)
            {
                _logger.LogError(
                    fallbackEx,
                    "Failed to send rules fallback message in chat {ChatId}",
                    chatId);

                return (DmSent: false, DmFallback: false);
            }
        }
    }

    private async Task HandleUserLeftAsync(long chatId, long userId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "User {UserId} left chat {ChatId}, recording welcome response if pending",
            userId,
            chatId);

        try
        {
            // Find any pending welcome response for this user
            var response = await WithRepositoryAsync((repo, ct) => repo.GetByUserAndChatAsync(userId, chatId, ct), cancellationToken);

            if (response == null || response.Response != WelcomeResponseType.Pending)
            {
                _logger.LogDebug(
                    "No pending welcome response found for user {UserId} in chat {ChatId}",
                    userId,
                    chatId);
                return;
            }

            // Cancel timeout job if it exists
            if (response.TimeoutJobId.HasValue)
            {
                try
                {
                    using var tickerScope = _serviceProvider.CreateScope();
                    var timeTickerManager = tickerScope.ServiceProvider.GetRequiredService<ITimeTickerManager<TimeTicker>>();

                    await timeTickerManager.DeleteAsync(response.TimeoutJobId.Value);

                    _logger.LogInformation(
                        "Cancelled timeout job {JobId} for user {UserId} who left chat {ChatId}",
                        response.TimeoutJobId.Value,
                        userId,
                        chatId);

                    await WithRepositoryAsync((repo, ct) => repo.SetTimeoutJobIdAsync(response.Id, null, ct), cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Failed to cancel timeout job {JobId} for user {UserId} who left",
                        response.TimeoutJobId.Value,
                        userId);
                    // Non-fatal - continue with marking as left
                }
            }

            // Mark as left
            await WithRepositoryAsync((repo, ct) => repo.UpdateResponseAsync(response.Id, WelcomeResponseType.Left, dmSent: false, dmFallback: false, ct), cancellationToken);

            _logger.LogInformation(
                "Recorded welcome response 'left' for user {UserId} in chat {ChatId}",
                userId,
                chatId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to handle user left for user {UserId} in chat {ChatId}",
                userId,
                chatId);
        }
    }
}
