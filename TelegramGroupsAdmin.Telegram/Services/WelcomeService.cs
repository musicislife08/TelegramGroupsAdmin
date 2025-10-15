using Microsoft.EntityFrameworkCore;
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
using TelegramGroupsAdmin.Telegram.Jobs;
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
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<WelcomeService> _logger;
    private readonly TelegramOptions _telegramOptions;
    private readonly IConfigService _configService;
    private readonly ITimeTickerManager<TimeTicker> _timeTickerManager;

    public WelcomeService(
        IDbContextFactory<AppDbContext> contextFactory,
        ILogger<WelcomeService> logger,
        IOptions<TelegramOptions> telegramOptions,
        IConfigService configService,
        ITimeTickerManager<TimeTicker> timeTickerManager)
    {
        _contextFactory = contextFactory;
        _logger = logger;
        _telegramOptions = telegramOptions.Value;
        _configService = configService;
        _timeTickerManager = timeTickerManager;
    }

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

        // Skip bots
        if (user.IsBot)
        {
            _logger.LogDebug("Skipping welcome for bot user {UserId}", user.Id);
            return;
        }

        _logger.LogInformation(
            "New user joined: {UserId} (@{Username}) in chat {ChatId}",
            user.Id,
            user.Username,
            chatMemberUpdate.Chat.Id);

        // Load welcome config from database (chat-specific or global fallback)
        var config = await _configService.GetEffectiveAsync<WelcomeConfig>("welcome", chatMemberUpdate.Chat.Id)
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
            await using (var context = await _contextFactory.CreateDbContextAsync(cancellationToken))
            {
                var entity = new Data.Models.WelcomeResponseDto
                {
                    ChatId = chatMemberUpdate.Chat.Id,
                    UserId = user.Id,
                    Username = user.Username,
                    WelcomeMessageId = welcomeMessage.MessageId,
                    Response = "pending",
                    RespondedAt = DateTimeOffset.UtcNow,
                    DmSent = false,
                    DmFallback = false,
                    CreatedAt = DateTimeOffset.UtcNow
                };

                context.WelcomeResponses.Add(entity);
                await context.SaveChangesAsync(cancellationToken);

                _logger.LogDebug(
                    "Created welcome response record for user {UserId} in chat {ChatId}",
                    user.Id,
                    chatMemberUpdate.Chat.Id);
            }

            // Step 4: Schedule timeout via TickerQ (replaces fire-and-forget Task.Run)
            var payload = new WelcomeTimeoutJob.TimeoutPayload(
                chatMemberUpdate.Chat.Id,
                user.Id,
                welcomeMessage.MessageId
            );

            await _timeTickerManager.AddAsync(new TimeTicker
            {
                Function = "WelcomeTimeout",
                ExecutionTime = DateTime.UtcNow.AddSeconds(config.TimeoutSeconds),
                Request = TickerHelper.CreateTickerRequest(payload),
                Retries = 1,
                RetryIntervals = [30] // Retry once after 30s if it fails
            });

            _logger.LogInformation(
                "Scheduled welcome timeout for user {UserId} in chat {ChatId} (timeout: {Timeout}s)",
                user.Id,
                chatMemberUpdate.Chat.Id,
                config.TimeoutSeconds);
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
                var deletePayload = new DeleteMessageJob.DeletePayload(
                    chatId,
                    warningMsg.MessageId,
                    "wrong_user_warning"
                );

                await _timeTickerManager.AddAsync(new TimeTicker
                {
                    Function = "DeleteMessage",
                    ExecutionTime = DateTime.UtcNow.AddSeconds(10),
                    Request = TickerHelper.CreateTickerRequest(deletePayload),
                    Retries = 0 // Don't retry - message may have been manually deleted
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send warning message");
            }

            return;
        }

        // Load welcome config from database (chat-specific or global fallback)
        var config = await _configService.GetEffectiveAsync<WelcomeConfig>("welcome", chatId)
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
        CancellationToken cancellationToken)
    {
        var username = user.Username != null ? $"@{user.Username}" : user.FirstName;
        var messageText = config.ChatWelcomeTemplate.Replace("{username}", username);

        // Get bot username for deep link
        var botInfo = await botClient.GetMe(cancellationToken);
        var deepLink = $"https://t.me/{botInfo.Username}?start=welcome_{chatId}_{user.Id}";

        // Encode user ID in callback data for validation + deep link button for DM
        // DM button on top to encourage private rules delivery
        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithUrl("📖 Read Rules (Opens Bot Chat)", deepLink)
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData(config.AcceptButtonText, $"welcome_accept:{user.Id}"),
                InlineKeyboardButton.WithCallbackData(config.DenyButtonText, $"welcome_deny:{user.Id}")
            }
        });

        var message = await botClient.SendMessage(
            chatId: chatId,
            text: messageText,
            replyMarkup: keyboard,
            cancellationToken: cancellationToken);

        _logger.LogInformation(
            "Sent welcome message {MessageId} to user {UserId} in chat {ChatId} with deep link: {DeepLink}",
            message.MessageId,
            user.Id,
            chatId,
            deepLink);

        return message;
    }

    private async Task RestrictUserPermissionsAsync(
        ITelegramBotClient botClient,
        long chatId,
        long userId,
        CancellationToken cancellationToken)
    {
        try
        {
            var permissions = new ChatPermissions
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

            await botClient.RestrictChatMember(
                chatId: chatId,
                userId: userId,
                permissions: permissions,
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
        CancellationToken cancellationToken)
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

            var permissions = new ChatPermissions
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
                CanChangeInfo = false,
                CanInviteUsers = true,
                CanPinMessages = false,
                CanManageTopics = false
            };

            await botClient.RestrictChatMember(
                chatId: chatId,
                userId: userId,
                permissions: permissions,
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
        CancellationToken cancellationToken)
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
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "User {UserId} (@{Username}) accepted rules in chat {ChatId}",
            user.Id,
            user.Username,
            chatId);

        // Step 1: Check if user already responded (from pending record created on join)
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var existingResponse = await context.WelcomeResponses
            .Where(r => r.ChatId == chatId && r.UserId == user.Id && r.WelcomeMessageId == welcomeMessageId)
            .FirstOrDefaultAsync(cancellationToken);

        // Step 2: Try to send rules via DM (or fallback to chat)
        // Always attempt this - previous DM sent via /start may have been deleted by user
        var (dmSent, dmFallback) = await SendRulesAsync(botClient, chatId, user, config, cancellationToken);

        _logger.LogInformation(
            "Rules delivery for user {UserId}: DM sent: {DmSent}, Fallback: {DmFallback}",
            user.Id,
            dmSent,
            dmFallback);

        // Step 3: Restore user permissions
        await RestoreUserPermissionsAsync(botClient, chatId, user.Id, cancellationToken);

        // Step 4: Delete welcome message
        try
        {
            await botClient.DeleteMessage(chatId: chatId, messageId: welcomeMessageId, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete welcome message {MessageId}", welcomeMessageId);
        }

        // Step 5: Update or create response record
        if (existingResponse != null)
        {
            // Update existing record (from /start deep link flow)
            existingResponse.Response = "accepted";
            existingResponse.RespondedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            // Create new record (direct accept without /start)
            var entity = new Data.Models.WelcomeResponseDto
            {
                ChatId = chatId,
                UserId = user.Id,
                Username = user.Username,
                WelcomeMessageId = welcomeMessageId,
                Response = "accepted",
                RespondedAt = DateTimeOffset.UtcNow,
                DmSent = dmSent,
                DmFallback = dmFallback,
                CreatedAt = DateTimeOffset.UtcNow
            };
            context.WelcomeResponses.Add(entity);
        }

        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Recorded welcome response: User {UserId} (@{Username}) in chat {ChatId} - Accepted (DM: {DmSent}, Fallback: {DmFallback})",
            user.Id, user.Username, chatId, dmSent, dmFallback);
    }

    private async Task HandleDenyAsync(
        ITelegramBotClient botClient,
        long chatId,
        User user,
        int welcomeMessageId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "User {UserId} (@{Username}) denied rules in chat {ChatId}",
            user.Id,
            user.Username,
            chatId);

        // Step 1: Kick user
        await KickUserAsync(botClient, chatId, user.Id, cancellationToken);

        // Step 2: Delete welcome message
        try
        {
            await botClient.DeleteMessage(chatId: chatId, messageId: welcomeMessageId, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete welcome message {MessageId}", welcomeMessageId);
        }

        // Step 3: Record response
        await using var context = await _contextFactory.CreateDbContextAsync();
        var entity = new Data.Models.WelcomeResponseDto
        {
            ChatId = chatId,
            UserId = user.Id,
            Username = user.Username,
            WelcomeMessageId = welcomeMessageId,
            Response = "denied",
            RespondedAt = DateTimeOffset.UtcNow,
            DmSent = false,
            DmFallback = false,
            CreatedAt = DateTimeOffset.UtcNow
        };

        context.WelcomeResponses.Add(entity);
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Recorded welcome response: User {UserId} (@{Username}) in chat {ChatId} - Denied",
            user.Id, user.Username, chatId);
    }

    private async Task HandleDmAcceptAsync(
        ITelegramBotClient botClient,
        long groupChatId,
        User user,
        long dmChatId,
        int buttonMessageId,
        CancellationToken cancellationToken)
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

        // Step 2: Find the welcome message in database
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var welcomeResponse = await context.WelcomeResponses
            .Where(r => r.ChatId == groupChatId && r.UserId == user.Id)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

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

        // Step 3: Restore user permissions in group
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

        // Step 4: Delete welcome message in group
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

        // Step 5: Update welcome response record (mark as accepted)
        welcomeResponse.Response = "accepted";
        welcomeResponse.RespondedAt = DateTimeOffset.UtcNow;
        welcomeResponse.DmSent = true;
        welcomeResponse.DmFallback = false;

        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Updated welcome response: User {UserId} (@{Username}) in chat {ChatId} - Accepted via DM",
            user.Id,
            user.Username,
            groupChatId);

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

    private async Task<string> GetChatNameAsync(ITelegramBotClient botClient, long chatId, CancellationToken cancellationToken)
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
        CancellationToken cancellationToken)
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
                var fallbackDeletePayload = new DeleteMessageJob.DeletePayload(
                    chatId,
                    fallbackMessage.MessageId,
                    "fallback_rules"
                );

                await _timeTickerManager.AddAsync(new TimeTicker
                {
                    Function = "DeleteMessage",
                    ExecutionTime = DateTime.UtcNow.AddSeconds(30),
                    Request = TickerHelper.CreateTickerRequest(fallbackDeletePayload),
                    Retries = 0 // Don't retry - message may have been manually deleted
                });

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

    private async Task HandleUserLeftAsync(long chatId, long userId, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "User {UserId} left chat {ChatId}, recording welcome response if pending",
            userId,
            chatId);

        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            // Find any pending welcome response for this user
            var response = await context.WelcomeResponses
                .Where(r => r.ChatId == chatId && r.UserId == userId && r.Response == "pending")
                .OrderByDescending(r => r.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (response == null)
            {
                _logger.LogDebug(
                    "No pending welcome response found for user {UserId} in chat {ChatId}",
                    userId,
                    chatId);
                return;
            }

            // Mark as left
            response.Response = "left";
            response.RespondedAt = DateTimeOffset.UtcNow;
            await context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Recorded welcome response 'left' for user {UserId} in chat {ChatId}",
                userId,
                chatId);

            // Note: Timeout job will automatically skip when it sees response != "pending"
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
