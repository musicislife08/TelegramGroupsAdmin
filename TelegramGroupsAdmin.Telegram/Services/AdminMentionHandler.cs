using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Bot;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Handles @admin mentions in group chats by notifying all active administrators
/// Uses entity-based text_mention entities to support users without usernames, avoiding HTML injection risks.
/// </summary>
public class AdminMentionHandler
{
    private readonly ILogger<AdminMentionHandler> _logger;
    private readonly IChatAdminsRepository _chatAdminsRepository;
    private readonly IBotUserService _userService;
    private readonly IBotMessageService _messageService;

    public AdminMentionHandler(
        ILogger<AdminMentionHandler> logger,
        IChatAdminsRepository chatAdminsRepository,
        IBotUserService userService,
        IBotMessageService messageService)
    {
        _logger = logger;
        _chatAdminsRepository = chatAdminsRepository;
        _userService = userService;
        _messageService = messageService;
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
    /// Send notification to all admins in the chat by replying to the message with entity text_mentions
    /// </summary>
    public async Task NotifyAdminsAsync(
        Message message,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Get bot ID for filtering
            var botId = await _userService.GetBotIdAsync(cancellationToken);

            // Get all active admins for this chat
            var admins = await _chatAdminsRepository.GetChatAdminsAsync(message.Chat.Id, cancellationToken);

            if (admins.Count == 0)
            {
                _logger.LogWarning(
                    "No admins found in chat {ChatId} for @admin mention",
                    message.Chat.Id);
                return;
            }

            // Build entity-based notification with text_mention entities for each admin
            var builder = new TelegramMessageBuilder().Bold("🔔 Admin Alert").LineBreak();
            var notified = 0;

            foreach (var admin in admins)
            {
                // Skip the user who sent the @admin mention
                if (admin.User.Id == message.From?.Id)
                    continue;

                // Skip the bot itself (bots can't receive notifications anyway)
                if (admin.User.Id == botId)
                    continue;

                if (notified > 0) builder.Text(" ");
                builder.Mention(admin.User);
                notified++;
            }

            if (notified == 0)
            {
                _logger.LogInformation(
                    "No other admins to notify in chat {ChatId} (only sender is admin)",
                    message.Chat.Id);
                return;
            }

            builder.Text(" you've been mentioned in this conversation.");

            // Reply to the original message with admin mentions
            await _messageService.SendAndSaveMessageAsync(
                chatId: message.Chat.Id,
                message: builder.Build(),
                replyParameters: new ReplyParameters { MessageId = message.MessageId },
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Notified {AdminCount} admins in {Chat} for @admin mention by {User}",
                notified,
                message.Chat.ToLogInfo(),
                message.From.ToLogInfo());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error notifying admins in chat {ChatId} for @admin mention",
                message.Chat.Id);
        }
    }
}
