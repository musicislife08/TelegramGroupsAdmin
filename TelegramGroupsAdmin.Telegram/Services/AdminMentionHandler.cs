using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Identity;

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
    private readonly IUserIdentityService _identityService;
    private readonly IConfigService _configService;

    public AdminMentionHandler(
        ILogger<AdminMentionHandler> logger,
        IChatAdminsRepository chatAdminsRepository,
        IBotUserService userService,
        IBotMessageService messageService,
        IUserIdentityService identityService,
        IConfigService configService)
    {
        _logger = logger;
        _chatAdminsRepository = chatAdminsRepository;
        _userService = userService;
        _messageService = messageService;
        _identityService = identityService;
        _configService = configService;
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

            // Skip the user who sent the @admin mention, and the bot itself (bots can't
            // receive notifications anyway)
            var adminIds = admins
                .Select(admin => admin.User.Id)
                .Where(id => id != message.From?.Id && id != botId)
                .ToList();

            if (adminIds.Count == 0)
            {
                _logger.LogInformation(
                    "No other admins to notify in chat {ChatId} (only sender is admin)",
                    message.Chat.Id);
                return;
            }

            // Mention each admin by their resolved identity (current names and verdict), with
            // this chat's name masking applied
            var adminIdentities = await _identityService.ResolveManyAsync(adminIds, cancellationToken);
            var masking = await _configService.GetNameMaskingAsync(message.Chat.Id, cancellationToken);
            var builder = TelegramMessageBuilder.For(masking).Bold("🔔 Admin Alert").LineBreak();

            for (var i = 0; i < adminIdentities.Count; i++)
            {
                if (i > 0) builder.Text(" ");
                builder.Mention(adminIdentities[i]);
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
                adminIdentities.Count,
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
