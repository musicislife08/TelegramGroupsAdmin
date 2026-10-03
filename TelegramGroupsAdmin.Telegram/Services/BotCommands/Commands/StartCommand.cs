using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.DmCelebrations;
using TelegramGroupsAdmin.Telegram.Services.Identity;
using TelegramGroupsAdmin.Telegram.Services.Welcome;
using TelegramGroupsAdmin.Configuration.Services;

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
    private readonly IBanCelebrationSubscriptionService _celebrationSubscriptionService;
    private readonly IUserIdentityService _identityService;

    public StartCommand(
        ILogger<StartCommand> logger,
        IWelcomeResponsesRepository welcomeResponsesRepository,
        ITelegramUserRepository telegramUserRepository,
        IPendingNotificationsRepository pendingNotificationsRepository,
        IServiceProvider serviceProvider,
        IBotMessageService messageService,
        IBotChatService chatService,
        IBotDmService dmService,
        IBanCelebrationSubscriptionService celebrationSubscriptionService,
        IUserIdentityService identityService)
    {
        _logger = logger;
        _welcomeResponsesRepository = welcomeResponsesRepository;
        _telegramUserRepository = telegramUserRepository;
        _pendingNotificationsRepository = pendingNotificationsRepository;
        _serviceProvider = serviceProvider;
        _messageService = messageService;
        _chatService = chatService;
        _dmService = dmService;
        _celebrationSubscriptionService = celebrationSubscriptionService;
        _identityService = identityService;
    }

    public string Name => "start";
    public string Description => "Start conversation with bot";
    public string Usage => "/start [deeplink_payload]";
    public PermissionLevel MinPermissionLevel => PermissionLevel.Member; // everyone
    public bool RequiresReply => false;
    public bool DeleteCommandMessage => false;
    public int? DeleteResponseAfterSeconds => null;

    public async Task<CommandResult> ExecuteAsync(
        Message message,
        string[] args,
        PermissionLevel userPermission,
        CancellationToken cancellationToken = default)
    {
        // Only respond to /start in private DMs, ignore in group chats
        if (message.Chat.Type != ChatType.Private)
        {
            return new CommandResult(TelegramMessage.Empty, DeleteCommandMessage, DeleteResponseAfterSeconds); // Silently ignore /start in group chats
        }

        // User started a private conversation with the bot - enable DM notifications
        // This allows the bot to send private messages to this user in the future.
        // Done before resolving the sender so a resolve failure cannot stop it.
        if (message.From != null)
        {
            await _telegramUserRepository.EnableBotDmAsync(message.From.Id, cancellationToken);
        }

        // DMs are not observed (a DM rename recorded without a rescan would use the rename up),
        // so the sender is resolved by id once and passed to every branch below.
        var sender = message.From != null
            ? await _identityService.ResolveAsync(message.From.Id, cancellationToken)
            : null;

        if (sender != null)
        {
            // Deliver any pending notifications
            await DeliverPendingNotificationsAsync(sender, cancellationToken);
        }

        // Deep link from the /dmcelebrations start prompt
        if (args.Length > 0 && sender != null &&
            DmCelebrationDeepLink.TryParseChatId(args[0], out var celebrationChatId))
        {
            return await HandleDmCelebrationsDeepLinkAsync(sender, celebrationChatId, cancellationToken);
        }

        // Check if this is a deep link for welcome system
        if (args.Length > 0 && args[0].StartsWith("welcome_"))
        {
            return await HandleWelcomeDeepLinkAsync(message, sender, args[0], cancellationToken);
        }

        // Check if this is a deep link for entrance exam
        if (args.Length > 0 && WelcomeDeepLinkBuilder.IsExamPayload(args[0]))
        {
            return await HandleExamDeepLinkAsync(message, sender, args[0], cancellationToken);
        }

        // Default /start response
        return new CommandResult(
            TelegramMessage.Plain(
                "👋 Welcome to TelegramGroupsAdmin Bot!\n\n" +
                "This bot helps manage your Telegram groups with spam detection and moderation tools.\n\n" +
                "Use /help to see available commands."),
            DeleteCommandMessage,
            DeleteResponseAfterSeconds);
    }

    private async Task<CommandResult> HandleDmCelebrationsDeepLinkAsync(
        UserIdentity from,
        long chatId,
        CancellationToken cancellationToken)
    {
        var chat = await _celebrationSubscriptionService.ConfirmFromStartAsync(chatId, from, cancellationToken);

        var reply = chat is null
            ? "You're not signed up for ban celebrations from that chat. Run /dmcelebrations on in the group to sign up."
            : $"🎉 You're all set — you'll get {chat.ChatName ?? "that chat"}'s ban celebrations here.";

        return new CommandResult(TelegramMessage.Plain(reply), DeleteCommandMessage, DeleteResponseAfterSeconds);
    }

    private async Task<CommandResult> HandleWelcomeDeepLinkAsync(
        Message message,
        UserIdentity? sender,
        string payload,
        CancellationToken cancellationToken)
    {
        // Parse payload: "welcome_chatId_userId"
        var parts = payload.Split('_');
        if (parts.Length != 3 || !long.TryParse(parts[1], out var chatId) || !long.TryParse(parts[2], out var targetUserId))
        {
            return new CommandResult(
                TelegramMessage.Plain("❌ Invalid deep link. Please use the button from the welcome message."),
                DeleteCommandMessage,
                DeleteResponseAfterSeconds);
        }

        // Verify the user clicking is the target user
        if (sender?.Id != targetUserId)
        {
            return new CommandResult(
                TelegramMessage.Plain("❌ This link is not for you. Please use the welcome link sent to you."),
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
                TelegramMessage.Plain("❌ Unable to retrieve chat information. The bot may have been removed from the chat."),
                DeleteCommandMessage,
                DeleteResponseAfterSeconds);
        }

        // Load welcome config from database (chat-specific or global fallback)
        // Must create scope because StartCommand is scoped but IConfigService is also scoped
        using var scope = _serviceProvider.CreateScope();
        var configService = scope.ServiceProvider.GetRequiredService<IConfigService>();
        var config = await configService.GetEffectiveWelcomeAsync(chatId, cancellationToken)
                     ?? WelcomeConfig.Default;

        // Send main welcome message in DM — built through the shared WelcomeMessageBuilder so the
        // {username}/{chat_name}/{timeout} substitution (clickable mention, humanized timeout)
        // matches every other welcome render path.
        var chatName = chat.Title ?? "the chat";
        var welcomeMessage = WelcomeMessageBuilder.BuildFromTemplate(
            config.MainWelcomeMessage,
            sender,
            chatName,
            config.TimeoutSeconds,
            await configService.GetNameMaskingAsync(null, cancellationToken));

        await _messageService.SendAndSaveMessageAsync(
            chatId: message.Chat.Id,
            message: welcomeMessage,
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
        return new CommandResult(TelegramMessage.Empty, DeleteCommandMessage, DeleteResponseAfterSeconds);
    }

    /// <summary>
    /// Handle exam deep link - starts entrance exam in DM
    /// </summary>
    private async Task<CommandResult> HandleExamDeepLinkAsync(
        Message message,
        UserIdentity? sender,
        string payload,
        CancellationToken cancellationToken)
    {
        // Parse payload: "exam_start_chatId_userId"
        var examPayload = WelcomeDeepLinkBuilder.ParseExamStartPayload(payload);
        if (examPayload == null)
        {
            return new CommandResult(
                TelegramMessage.Plain("❌ Invalid exam link. Please use the button from the welcome message."),
                DeleteCommandMessage,
                DeleteResponseAfterSeconds);
        }

        // Verify the user clicking is the target user
        if (sender?.Id != examPayload.UserId)
        {
            return new CommandResult(
                TelegramMessage.Plain("❌ This exam link is not for you. Please use the button sent to you when you joined."),
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
                TelegramMessage.Plain("❌ Unable to retrieve chat information. The bot may have been removed from the chat."),
                DeleteCommandMessage,
                DeleteResponseAfterSeconds);
        }

        // Load welcome config from database (chat-specific or global fallback)
        using var scope = _serviceProvider.CreateScope();
        var configService = scope.ServiceProvider.GetRequiredService<IConfigService>();
        var config = await configService.GetEffectiveWelcomeAsync(examPayload.ChatId, cancellationToken)
                     ?? WelcomeConfig.Default;

        if (config.Mode != WelcomeMode.EntranceExam || config.ExamConfig == null)
        {
            return new CommandResult(
                TelegramMessage.Plain("❌ Entrance exam is no longer configured for this chat."),
                DeleteCommandMessage,
                DeleteResponseAfterSeconds);
        }

        // Start exam in DM - questions will be sent to this private chat
        var examFlowService = scope.ServiceProvider.GetRequiredService<IExamFlowService>();
        var result = await examFlowService.StartExamInDmAsync(
            chat: ChatIdentity.From(chat),
            user: sender,
            dmChatId: message.Chat.Id,  // User's private chat with bot
            config: config,
            cancellationToken: cancellationToken);

        if (!result.Success)
        {
            return new CommandResult(
                TelegramMessage.Plain("❌ Failed to start exam. Please try again or contact an admin."),
                DeleteCommandMessage,
                DeleteResponseAfterSeconds);
        }

        _logger.LogInformation(
            "Started entrance exam in DM for {User} from {Chat}",
            sender.ToLogInfo(),
            chat.ToLogInfo());

        // Empty result - the exam service sends the first question
        return new CommandResult(TelegramMessage.Empty, DeleteCommandMessage, DeleteResponseAfterSeconds);
    }

    /// <summary>
    /// Deliver all pending notifications to user when they enable DMs
    /// </summary>
    private async Task DeliverPendingNotificationsAsync(
        UserIdentity user,
        CancellationToken cancellationToken)
    {
        var telegramUserId = user.Id;
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
                        user: user,
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
