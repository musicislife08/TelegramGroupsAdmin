using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Notifications;

namespace TelegramGroupsAdmin.Telegram.Services.BotCommands.Commands;

/// <summary>
/// /start - Handle deep links for welcome system and enable bot DM notifications
/// </summary>
public class StartCommand : IBotCommand
{
    private readonly ILogger<StartCommand> _logger;
    private readonly IWelcomeResponsesRepository _welcomeResponsesRepository;
    private readonly ITelegramUserRepository _telegramUserRepository;
    private readonly IPendingNotificationsRepository _pendingNotificationsRepository;
    private readonly IServiceProvider _serviceProvider;

    public StartCommand(
        ILogger<StartCommand> logger,
        IWelcomeResponsesRepository welcomeResponsesRepository,
        ITelegramUserRepository telegramUserRepository,
        IPendingNotificationsRepository pendingNotificationsRepository,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _welcomeResponsesRepository = welcomeResponsesRepository;
        _telegramUserRepository = telegramUserRepository;
        _pendingNotificationsRepository = pendingNotificationsRepository;
        _serviceProvider = serviceProvider;
    }

    public string Name => "start";
    public string Description => "Start conversation with bot";
    public string Usage => "/start [deeplink_payload]";
    public int MinPermissionLevel => 0; // Everyone can use
    public bool RequiresReply => false;
    public bool DeleteCommandMessage => false;
    public int? DeleteResponseAfterSeconds => null;

    public async Task<CommandResult> ExecuteAsync(
        ITelegramBotClient botClient,
        Message message,
        string[] args,
        int userPermissionLevel,
        CancellationToken cancellationToken = default)
    {
        // Only respond to /start in private DMs, ignore in group chats
        if (message.Chat.Type != ChatType.Private)
        {
            return new CommandResult(string.Empty, DeleteCommandMessage, DeleteResponseAfterSeconds); // Silently ignore /start in group chats
        }

        // User started a private conversation with the bot - enable DM notifications
        // This allows the bot to send private messages to this user in the future
        if (message.From != null)
        {
            await _telegramUserRepository.SetBotDmEnabledAsync(message.From.Id, enabled: true, cancellationToken);

            // Deliver any pending notifications
            await DeliverPendingNotificationsAsync(botClient, message.From.Id, cancellationToken);
        }

        // Check if this is a deep link for welcome system
        if (args.Length > 0 && args[0].StartsWith("welcome_"))
        {
            return await HandleWelcomeDeepLinkAsync(botClient, message, args[0], cancellationToken);
        }

        // Default /start response
        return new CommandResult(
            "👋 Welcome to TelegramGroupsAdmin Bot!\n\n" +
            "This bot helps manage your Telegram groups with spam detection and moderation tools.\n\n" +
            "Use /help to see available commands.",
            DeleteCommandMessage,
            DeleteResponseAfterSeconds);
    }

    private async Task<CommandResult> HandleWelcomeDeepLinkAsync(
        ITelegramBotClient botClient,
        Message message,
        string payload,
        CancellationToken cancellationToken)
    {
        // Parse payload: "welcome_chatId_userId"
        var parts = payload.Split('_');
        if (parts.Length != 3 || !long.TryParse(parts[1], out var chatId) || !long.TryParse(parts[2], out var targetUserId))
        {
            return new CommandResult(
                "❌ Invalid deep link. Please use the button from the welcome message.",
                DeleteCommandMessage,
                DeleteResponseAfterSeconds);
        }

        // Verify the user clicking is the target user
        if (message.From?.Id != targetUserId)
        {
            return new CommandResult(
                "❌ This link is not for you. Please use the welcome link sent to you.",
                DeleteCommandMessage,
                DeleteResponseAfterSeconds);
        }

        // Get chat info
        Chat chat;
        try
        {
            chat = await botClient.GetChat(chatId, cancellationToken);
        }
        catch (Exception)
        {
            return new CommandResult(
                "❌ Unable to retrieve chat information. The bot may have been removed from the chat.",
                DeleteCommandMessage,
                DeleteResponseAfterSeconds);
        }

        // Get welcome config (using default for now - TODO: load from database)
        var config = WelcomeConfig.Default;

        // Send main welcome message in DM
        var chatName = chat.Title ?? "the chat";
        var username = message.From.Username != null ? $"@{message.From.Username}" : message.From.FirstName;

        var messageText = config.MainWelcomeMessage
            .Replace("{username}", username)
            .Replace("{chat_name}", chatName)
            .Replace("{timeout}", config.TimeoutSeconds.ToString());

        await botClient.SendMessage(
            chatId: message.Chat.Id,
            text: messageText,
            cancellationToken: cancellationToken);

        // Send Accept button in separate message (will be deleted after click)
        // Format: dm_accept:chatId:userId
        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    "✅ I Accept These Rules",
                    $"dm_accept:{chatId}:{targetUserId}")
            }
        });

        await botClient.SendMessage(
            chatId: message.Chat.Id,
            text: "👇 Click below to accept the rules:",
            replyMarkup: keyboard,
            cancellationToken: cancellationToken);

        // Mark as DM sent in database (update the welcome response record)
        var welcomeResponse = await _welcomeResponsesRepository.GetByUserAndChatAsync(targetUserId, chatId, cancellationToken);

        if (welcomeResponse != null)
        {
            await _welcomeResponsesRepository.UpdateResponseAsync(
                welcomeResponse.Id,
                welcomeResponse.Response, // Keep existing response status
                dmSent: true,
                dmFallback: false,
                cancellationToken);
        }

        // Don't return a message - the Accept button will trigger the final confirmation
        return new CommandResult(string.Empty, DeleteCommandMessage, DeleteResponseAfterSeconds);
    }

    /// <summary>
    /// Deliver all pending notifications to user when they enable DMs
    /// </summary>
    private async Task DeliverPendingNotificationsAsync(
        ITelegramBotClient botClient,
        long telegramUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            var pendingNotifications = await _pendingNotificationsRepository.GetPendingNotificationsForUserAsync(
                telegramUserId,
                cancellationToken);

            if (!pendingNotifications.Any())
            {
                return; // No pending notifications
            }

            _logger.LogInformation(
                "Delivering {Count} pending notifications to user {UserId}",
                pendingNotifications.Count,
                telegramUserId);

            // Send each pending notification
            foreach (var notification in pendingNotifications)
            {
                try
                {
                    await botClient.SendMessage(
                        chatId: telegramUserId,
                        text: notification.MessageText,
                        cancellationToken: cancellationToken);

                    // Delete successfully delivered notification
                    await _pendingNotificationsRepository.DeletePendingNotificationAsync(
                        notification.Id,
                        cancellationToken);

                    _logger.LogInformation(
                        "Delivered pending {NotificationType} notification {Id} to user {UserId}",
                        notification.NotificationType,
                        notification.Id,
                        telegramUserId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Failed to deliver pending notification {Id} to user {UserId}",
                        notification.Id,
                        telegramUserId);

                    // Increment retry count but keep in queue
                    await _pendingNotificationsRepository.IncrementRetryCountAsync(
                        notification.Id,
                        cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to process pending notifications for user {UserId}",
                telegramUserId);
        }
    }
}
