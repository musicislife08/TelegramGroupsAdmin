using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.Telegram.Services.BotCommands.Commands;

/// <summary>
/// /start - Handle deep links for welcome system and enable bot DM notifications
/// </summary>
public class StartCommand : IBotCommand
{
    private readonly IWelcomeResponsesRepository _welcomeResponsesRepository;
    private readonly TelegramUserRepository _telegramUserRepository;
    private readonly IServiceProvider _serviceProvider;

    public StartCommand(
        IWelcomeResponsesRepository welcomeResponsesRepository,
        TelegramUserRepository telegramUserRepository,
        IServiceProvider serviceProvider)
    {
        _welcomeResponsesRepository = welcomeResponsesRepository;
        _telegramUserRepository = telegramUserRepository;
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

        // Send rules text (without button)
        var chatName = chat.Title ?? "the chat";
        var rulesText = config.DmTemplate
            .Replace("{chat_name}", chatName)
            .Replace("{rules_text}", config.RulesText);

        await botClient.SendMessage(
            chatId: message.Chat.Id,
            text: rulesText,
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
}
