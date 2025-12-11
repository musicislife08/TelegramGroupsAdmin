using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Handles @admin mentions in group chats by notifying all active administrators
/// Uses HTML text mentions (tg://user?id=X) to support users without usernames
/// </summary>
public class AdminMentionHandler
{
    private readonly ILogger<AdminMentionHandler> _logger;
    private readonly IChatAdminsRepository _chatAdminsRepository;
    private readonly ITelegramBotClientFactory _botClientFactory;

    public AdminMentionHandler(
        ILogger<AdminMentionHandler> logger,
        IChatAdminsRepository chatAdminsRepository,
        ITelegramBotClientFactory botClientFactory)
    {
        _logger = logger;
        _chatAdminsRepository = chatAdminsRepository;
        _botClientFactory = botClientFactory;
    }

    /// <summary>
    /// Check if message contains @admin mention
    /// </summary>
    public bool ContainsAdminMention(string? messageText)
    {
        if (string.IsNullOrWhiteSpace(messageText))
            return false;

        return messageText.Contains("@admin", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Send notification to all admins in the chat by replying to the message with HTML text mentions
    /// </summary>
    public async Task NotifyAdminsAsync(
        Message message,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var operations = await _botClientFactory.GetOperationsAsync();

            // Get all active admins for this chat
            var admins = await _chatAdminsRepository.GetChatAdminsAsync(message.Chat.Id, cancellationToken);

            if (admins.Count == 0)
            {
                _logger.LogWarning(
                    "No admins found in chat {ChatId} for @admin mention",
                    message.Chat.Id);
                return;
            }

            // Build HTML notification with text mentions for each admin
            var mentionsList = new List<string>();

            foreach (var admin in admins)
            {
                // Skip the user who sent the @admin mention
                if (admin.TelegramId == message.From?.Id)
                    continue;

                // Skip the bot itself (bots can't receive notifications anyway)
                if (admin.TelegramId == operations.BotId)
                    continue;

                // Create HTML text mention with username or fallback to generic name
                // Format: <a href="tg://user?id=123">@username</a> or <a href="tg://user?id=123">Admin</a>
                var displayName = !string.IsNullOrWhiteSpace(admin.Username)
                    ? $"@{admin.Username}"
                    : "Admin";
                mentionsList.Add($"<a href=\"tg://user?id={admin.TelegramId}\">{displayName}</a>");
            }

            if (mentionsList.Count == 0)
            {
                _logger.LogInformation(
                    "No other admins to notify in chat {ChatId} (only sender is admin)",
                    message.Chat.Id);
                return;
            }

            // Build final notification message
            var notificationText = "🔔 <b>Admin Alert</b>\n" +
                                   string.Join(" ", mentionsList) + " " +
                                   "you've been mentioned in this conversation.";

            // Reply to the original message with admin mentions
            await operations.SendMessageAsync(
                chatId: message.Chat.Id,
                text: notificationText,
                parseMode: ParseMode.Html,
                replyParameters: new ReplyParameters { MessageId = message.MessageId },
                ct: cancellationToken);

            _logger.LogInformation(
                "Notified {AdminCount} admins in chat {ChatId} for @admin mention by user {UserId}",
                mentionsList.Count,
                message.Chat.Id,
                message.From?.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error notifying admins in chat {ChatId} for @admin mention",
                message.Chat.Id);
        }
    }
}
