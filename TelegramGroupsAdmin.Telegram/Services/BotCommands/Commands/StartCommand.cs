using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Welcome;

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
    private readonly IBotMessageService _messageService;
    private readonly IBotChatService _chatService;
    private readonly IBotDmService _dmService;

    public StartCommand(
        ILogger<StartCommand> logger,
        IWelcomeResponsesRepository welcomeResponsesRepository,
        ITelegramUserRepository telegramUserRepository,
        IPendingNotificationsRepository pendingNotificationsRepository,
        IServiceProvider serviceProvider,
        IBotMessageService messageService,
        IBotChatService chatService,
        IBotDmService dmService)
    {
        _logger = logger;
        _welcomeResponsesRepository = welcomeResponsesRepository;
        _telegramUserRepository = telegramUserRepository;
        _pendingNotificationsRepository = pendingNotificationsRepository;
        _serviceProvider = serviceProvider;
        _messageService = messageService;
        _chatService = chatService;
        _dmService = dmService;
    }

    public string Name => "start";
    public string Description => "Start conversation with bot";
    public string Usage => "/start [deeplink_payload]";
    public int MinPermissionLevel => 0; // Everyone can use
    public bool RequiresReply => false;
    public bool DeleteCommandMessage => false;
    public int? DeleteResponseAfterSeconds => null;

    public async Task<CommandResult> ExecuteAsync(
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
            await DeliverPendingNotificationsAsync(message.From.Id, cancellationToken);
        }

        // Check if this is a deep link for welcome system
        if (args.Length > 0 && args[0].StartsWith("welcome_"))
        {
            return await HandleWelcomeDeepLinkAsync(message, args[0], cancellationToken);
        }

        // Check if this is a deep link for entrance exam
        if (args.Length > 0 && WelcomeDeepLinkBuilder.IsExamPayload(args[0]))
        {
            return await HandleExamDeepLinkAsync(message, args[0], cancellationToken);
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
        ChatFullInfo chat;
        try
        {
            chat = await _chatService.GetChatAsync(chatId, cancellationToken);
        }
        catch (Exception)
        {
            return new CommandResult(
                "❌ Unable to retrieve chat information. The bot may have been removed from the chat.",
                DeleteCommandMessage,
                DeleteResponseAfterSeconds);
        }

        // Load welcome config from database (chat-specific or global fallback)
        // Must create scope because StartCommand is scoped but IConfigService is also scoped
        using var scope = _serviceProvider.CreateScope();
        var configService = scope.ServiceProvider.GetRequiredService<IConfigService>();
        var config = await configService.GetEffectiveAsync<WelcomeConfig>(ConfigType.Welcome, chatId)
                     ?? WelcomeConfig.Default;

        // Send main welcome message in DM
        var chatName = chat.Title ?? "the chat";
        // Use HTML mention format to create clickable user tag
        var username = message.From.Username != null
            ? $"<a href=\"tg://user?id={message.From.Id}\">@{message.From.Username}</a>"
            : $"<a href=\"tg://user?id={message.From.Id}\">{message.From.FirstName}</a>";

        var messageText = config.MainWelcomeMessage
            .Replace("{username}", username)
            .Replace("{chat_name}", chatName)
            .Replace("{timeout}", config.TimeoutSeconds.ToString());

        await _messageService.SendAndSaveMessageAsync(
            chatId: message.Chat.Id,
            text: messageText,
            parseMode: ParseMode.Html,
            cancellationToken: cancellationToken);

        // Send Accept button in separate message (will be deleted after click)
        // Format: dm_accept:chatId:userId
        var keyboard = new InlineKeyboardMarkup([
            [
                InlineKeyboardButton.WithCallbackData(
                    "✅ I Accept These Rules",
                    $"dm_accept:{chatId}:{targetUserId}")
            ]
        ]);

        await _messageService.SendAndSaveMessageAsync(
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
    /// Handle exam deep link - starts entrance exam in DM
    /// </summary>
    private async Task<CommandResult> HandleExamDeepLinkAsync(
        Message message,
        string payload,
        CancellationToken cancellationToken)
    {
        // Parse payload: "exam_start_chatId_userId"
        var examPayload = WelcomeDeepLinkBuilder.ParseExamStartPayload(payload);
        if (examPayload == null)
        {
            return new CommandResult(
                "❌ Invalid exam link. Please use the button from the welcome message.",
                DeleteCommandMessage,
                DeleteResponseAfterSeconds);
        }

        // Verify the user clicking is the target user
        if (message.From?.Id != examPayload.UserId)
        {
            return new CommandResult(
                "❌ This exam link is not for you. Please use the button sent to you when you joined.",
                DeleteCommandMessage,
                DeleteResponseAfterSeconds);
        }

        // Get chat info to verify bot is still in chat
        ChatFullInfo chat;
        try
        {
            chat = await _chatService.GetChatAsync(examPayload.ChatId, cancellationToken);
        }
        catch (Exception)
        {
            return new CommandResult(
                "❌ Unable to retrieve chat information. The bot may have been removed from the chat.",
                DeleteCommandMessage,
                DeleteResponseAfterSeconds);
        }

        // Load welcome config from database (chat-specific or global fallback)
        using var scope = _serviceProvider.CreateScope();
        var configService = scope.ServiceProvider.GetRequiredService<IConfigService>();
        var config = await configService.GetEffectiveAsync<WelcomeConfig>(ConfigType.Welcome, examPayload.ChatId)
                     ?? WelcomeConfig.Default;

        if (config.Mode != WelcomeMode.EntranceExam || config.ExamConfig == null)
        {
            return new CommandResult(
                "❌ Entrance exam is no longer configured for this chat.",
                DeleteCommandMessage,
                DeleteResponseAfterSeconds);
        }

        // Start exam in DM - questions will be sent to this private chat
        var examFlowService = scope.ServiceProvider.GetRequiredService<IExamFlowService>();
        var result = await examFlowService.StartExamInDmAsync(
            chat: ChatIdentity.From(chat),
            user: message.From,
            dmChatId: message.Chat.Id,  // User's private chat with bot
            config: config,
            cancellationToken: cancellationToken);

        if (!result.Success)
        {
            return new CommandResult(
                "❌ Failed to start exam. Please try again or contact an admin.",
                DeleteCommandMessage,
                DeleteResponseAfterSeconds);
        }

        _logger.LogInformation(
            "Started entrance exam in DM for user {UserId} from group {GroupId}",
            examPayload.UserId,
            examPayload.ChatId);

        // Empty result - the exam service sends the first question
        return new CommandResult(string.Empty, DeleteCommandMessage, DeleteResponseAfterSeconds);
    }

    /// <summary>
    /// Deliver all pending notifications to user when they enable DMs
    /// </summary>
    private async Task DeliverPendingNotificationsAsync(
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

            // Send each pending notification via DM service
            foreach (var notification in pendingNotifications)
            {
                try
                {
                    var result = await _dmService.SendDmAsync(
                        telegramUserId: telegramUserId,
                        messageText: notification.MessageText,
                        cancellationToken: cancellationToken);

                    if (result.DmSent)
                    {
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
                    else
                    {
                        // DM failed - increment retry count
                        await _pendingNotificationsRepository.IncrementRetryCountAsync(
                            notification.Id,
                            cancellationToken);
                    }
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
