using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Data;
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

    public WelcomeService(
        IDbContextFactory<AppDbContext> contextFactory,
        ILogger<WelcomeService> logger,
        IOptions<TelegramOptions> telegramOptions)
    {
        _contextFactory = contextFactory;
        _logger = logger;
        _telegramOptions = telegramOptions.Value;
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

        // TODO: Load welcome config from database (Phase 4.4 continuation)
        var config = WelcomeConfig.Default;

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

            // Step 3: Schedule timeout job (TickerQ integration - Phase 4.4 continuation)
            // TODO: Schedule timeout job to auto-kick if no response
            _logger.LogDebug(
                "TODO: Schedule timeout job for user {UserId} in chat {ChatId} (timeout: {Timeout}s)",
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

        // Parse callback data (format: "welcome_accept:123456" or "welcome_deny:123456")
        var parts = data.Split(':');
        if (parts.Length != 2 || !long.TryParse(parts[1], out var targetUserId))
        {
            _logger.LogWarning("Invalid callback data format: {Data}", data);
            return;
        }

        var action = parts[0];

        // Validate that the clicking user is the target user
        if (user.Id != targetUserId)
        {
            _logger.LogWarning(
                "Wrong user clicked button: User {ClickerId} clicked button for user {TargetUserId}",
                user.Id,
                targetUserId);

            // Send temporary warning message tagged to the wrong user
            try
            {
                var username = user.Username != null ? $"@{user.Username}" : user.FirstName;
                var warningMsg = await botClient.SendMessage(
                    chatId: chatId,
                    text: $"{username}, ⚠️ this button is not for you. Only the mentioned user can respond.",
                    replyParameters: new ReplyParameters { MessageId = message.MessageId },
                    cancellationToken: cancellationToken);

                // Delete warning after 10 seconds
                _ = Task.Run(async () =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(10));
                    try
                    {
                        await botClient.DeleteMessage(chatId: chatId, messageId: warningMsg.MessageId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Failed to delete warning message {MessageId}", warningMsg.MessageId);
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send warning message");
            }

            return;
        }

        // TODO: Load welcome config from database (Phase 4.4 continuation)
        var config = WelcomeConfig.Default;

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

        // Step 1: Restore user permissions
        await RestoreUserPermissionsAsync(botClient, chatId, user.Id, cancellationToken);

        // Step 2: Try to send rules via DM
        var (dmSent, dmFallback) = await SendRulesAsync(botClient, chatId, user, config, cancellationToken);

        // Step 3: Delete welcome message
        try
        {
            await botClient.DeleteMessage(chatId: chatId, messageId: welcomeMessageId, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete welcome message {MessageId}", welcomeMessageId);
        }

        // Step 4: Record response
        await using var context = await _contextFactory.CreateDbContextAsync();
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

    private async Task<(bool DmSent, bool DmFallback)> SendRulesAsync(
        ITelegramBotClient botClient,
        long chatId,
        User user,
        WelcomeConfig config,
        CancellationToken cancellationToken)
    {
        var chatName = "this chat"; // TODO: Get actual chat name from cache (Phase 4.4 continuation)
        var dmText = config.DmTemplate
            .Replace("{chat_name}", chatName)
            .Replace("{rules_text}", config.RulesText);

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

            // Fallback: Send rules in chat
            try
            {
                var fallbackText = config.ChatFallbackTemplate.Replace("{rules_text}", config.RulesText);
                var username = user.Username != null ? $"@{user.Username}" : user.FirstName;
                var message = $"{username}, {fallbackText}";

                await botClient.SendMessage(
                    chatId: chatId,
                    text: message,
                    cancellationToken: cancellationToken);

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
}
