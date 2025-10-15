using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Services.BotCommands.Commands;

/// <summary>
/// /start - Handle deep links for welcome system
/// </summary>
public class StartCommand : IBotCommand
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly IServiceProvider _serviceProvider;

    public StartCommand(
        IDbContextFactory<AppDbContext> contextFactory,
        IServiceProvider serviceProvider)
    {
        _contextFactory = contextFactory;
        _serviceProvider = serviceProvider;
    }

    public string Name => "start";
    public string Description => "Start conversation with bot";
    public string Usage => "/start [deeplink_payload]";
    public int MinPermissionLevel => 0; // Everyone can use
    public bool RequiresReply => false;
    public bool DeleteCommandMessage => false;

    public async Task<string> ExecuteAsync(
        ITelegramBotClient botClient,
        Message message,
        string[] args,
        int userPermissionLevel,
        CancellationToken cancellationToken = default)
    {
        // Only respond to /start in private DMs, ignore in group chats
        if (message.Chat.Type != ChatType.Private)
        {
            return string.Empty; // Silently ignore /start in group chats
        }

        // Check if this is a deep link for welcome system
        if (args.Length > 0 && args[0].StartsWith("welcome_"))
        {
            return await HandleWelcomeDeepLinkAsync(botClient, message, args[0], cancellationToken);
        }

        // Default /start response
        return "👋 Welcome to TelegramGroupsAdmin Bot!\n\n" +
               "This bot helps manage your Telegram groups with spam detection and moderation tools.\n\n" +
               "Use /help to see available commands.";
    }

    private async Task<string> HandleWelcomeDeepLinkAsync(
        ITelegramBotClient botClient,
        Message message,
        string payload,
        CancellationToken cancellationToken)
    {
        // Parse payload: "welcome_chatId_userId"
        var parts = payload.Split('_');
        if (parts.Length != 3 || !long.TryParse(parts[1], out var chatId) || !long.TryParse(parts[2], out var targetUserId))
        {
            return "❌ Invalid deep link. Please use the button from the welcome message.";
        }

        // Verify the user clicking is the target user
        if (message.From?.Id != targetUserId)
        {
            return "❌ This link is not for you. Please use the welcome link sent to you.";
        }

        // Get chat info
        Chat chat;
        try
        {
            chat = await botClient.GetChat(chatId, cancellationToken);
        }
        catch (Exception)
        {
            return "❌ Unable to retrieve chat information. The bot may have been removed from the chat.";
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
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var welcomeResponse = await context.WelcomeResponses
            .Where(r => r.ChatId == chatId && r.UserId == targetUserId)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (welcomeResponse != null)
        {
            welcomeResponse.DmSent = true;
            welcomeResponse.DmFallback = false;
            await context.SaveChangesAsync(cancellationToken);
        }

        // Don't return a message - the Accept button will trigger the final confirmation
        return string.Empty;
    }
}
