using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Bot;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Service for sending messages to specific users with DM-first, mention-fallback strategy.
/// Uses IBotDmService for DM attempts and IBotMessageService for chat mention fallback.
/// </summary>
public class UserMessagingService : IUserMessagingService
{
    private readonly ITelegramUserRepository _telegramUserRepository;
    private readonly IBotDmService _dmService;
    private readonly IBotMessageService _messageService;
    private readonly ILogger<UserMessagingService> _logger;

    public UserMessagingService(
        ITelegramUserRepository telegramUserRepository,
        IBotDmService dmService,
        IBotMessageService messageService,
        ILogger<UserMessagingService> logger)
    {
        _telegramUserRepository = telegramUserRepository;
        _dmService = dmService;
        _messageService = messageService;
        _logger = logger;
    }

    public async Task<MessageSendResult> SendToUserAsync(
        long userId,
        Chat chat,
        string messageText,
        int? replyToMessageId = null,
        CancellationToken cancellationToken = default)
    {
        // Get user's DM preference (optimization: skip DM attempt if user blocked bot)
        var user = await _telegramUserRepository.GetByTelegramIdAsync(userId, cancellationToken);
        var botDmEnabled = user?.BotDmEnabled ?? false;

        // Attempt DM if user has enabled it
        if (botDmEnabled)
        {
            // Try DM via IBotDmService (no fallback - we'll handle mention fallback ourselves)
            var dmResult = await _dmService.SendDmAsync(
                telegramUserId: userId,
                messageText: messageText,
                fallbackChatId: null,
                cancellationToken: cancellationToken);

            if (dmResult.DmSent)
            {
                _logger.LogInformation(
                    "Sent DM to user {User}: {MessagePreview}",
                    user.ToLogInfo(userId),
                    messageText.Length > 50 ? messageText[..50] + "..." : messageText);

                return new MessageSendResult(userId, Success: true, MessageDeliveryMethod.PrivateDm);
            }

            // DM failed (user blocked bot or error) - fall through to mention fallback
            _logger.LogDebug(
                "DM to {User} failed, falling back to chat mention",
                user.ToLogDebug(userId));
        }

        // Fallback: Send as chat mention
        return await SendChatMentionAsync(userId, chat, messageText, replyToMessageId, cancellationToken);
    }

    public async Task<List<MessageSendResult>> SendToMultipleUsersAsync(
        List<long> userIds,
        Chat chat,
        string messageText,
        int? replyToMessageId = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<MessageSendResult>();
        var failedDmUsers = new List<(long UserId, string Mention)>();

        // Try to send DMs to all users who have it enabled
        foreach (var userId in userIds)
        {
            var user = await _telegramUserRepository.GetByTelegramIdAsync(userId, cancellationToken);
            var botDmEnabled = user?.BotDmEnabled ?? false;

            if (botDmEnabled)
            {
                // Try DM via IBotDmService
                var dmResult = await _dmService.SendDmAsync(
                    telegramUserId: userId,
                    messageText: messageText,
                    fallbackChatId: null,
                    cancellationToken: cancellationToken);

                if (dmResult.DmSent)
                {
                    _logger.LogInformation(
                        "Sent DM to user {User}: {MessagePreview}",
                        user.ToLogInfo(userId),
                        messageText.Length > 50 ? messageText[..50] + "..." : messageText);

                    results.Add(new MessageSendResult(userId, Success: true, MessageDeliveryMethod.PrivateDm));
                }
                else
                {
                    // DM failed - add to batch mention list
                    var userMention = TelegramDisplayName.FormatMention(user?.FirstName, user?.LastName, user?.Username, userId);
                    failedDmUsers.Add((userId, userMention));
                }
            }
            else
            {
                // User doesn't have DM enabled, add to batch mention list
                var userMention = TelegramDisplayName.FormatMention(user?.FirstName, user?.LastName, user?.Username, userId);
                failedDmUsers.Add((userId, userMention));
            }
        }

        // If any users need chat mentions, send ONE message with all mentions
        if (failedDmUsers.Count > 0)
        {
            try
            {
                var mentions = string.Join(", ", failedDmUsers.Select(u => u.Mention));
                var chatMessage = $"{mentions}:\n\n{messageText}";

                await _messageService.SendAndSaveMessageAsync(
                    chatId: chat.Id,
                    text: chatMessage,
                    parseMode: ParseMode.Markdown,
                    replyParameters: replyToMessageId.HasValue
                        ? new ReplyParameters { MessageId = replyToMessageId.Value }
                        : null,
                    cancellationToken: cancellationToken);

                _logger.LogInformation(
                    "Sent batched chat mention to {UserCount} users in {Chat}",
                    failedDmUsers.Count,
                    chat.ToLogInfo());

                // Add success result for all users in the batch
                foreach (var (userId, _) in failedDmUsers)
                {
                    results.Add(new MessageSendResult(userId, Success: true, MessageDeliveryMethod.ChatMention));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to send batched chat mention to {UserCount} users in {Chat}",
                    failedDmUsers.Count,
                    chat.ToLogDebug());

                // Add failure results
                foreach (var (userId, _) in failedDmUsers)
                {
                    results.Add(new MessageSendResult(
                        userId,
                        Success: false,
                        MessageDeliveryMethod.Failed,
                        ErrorMessage: ex.Message));
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Send a message in the chat with user mention (fallback when DM unavailable)
    /// </summary>
    private async Task<MessageSendResult> SendChatMentionAsync(
        long userId,
        Chat chat,
        string messageText,
        int? replyToMessageId,
        CancellationToken cancellationToken)
    {
        // Get user info for mention
        var user = await _telegramUserRepository.GetByTelegramIdAsync(userId, cancellationToken);

        try
        {
            var userMention = TelegramDisplayName.FormatMention(user?.FirstName, user?.LastName, user?.Username, userId);
            var chatMessage = $"{userMention}: {messageText}";

            await _messageService.SendAndSaveMessageAsync(
                chatId: chat.Id,
                text: chatMessage,
                parseMode: ParseMode.Markdown,
                replyParameters: replyToMessageId.HasValue
                    ? new ReplyParameters { MessageId = replyToMessageId.Value }
                    : null,
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Sent chat mention to user {User} in {Chat}",
                user.ToLogInfo(userId),
                chat.ToLogInfo());

            return new MessageSendResult(userId, Success: true, MessageDeliveryMethod.ChatMention);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to send chat mention to user {User} in {Chat}",
                user.ToLogDebug(userId),
                chat.ToLogDebug());

            return new MessageSendResult(
                userId,
                Success: false,
                MessageDeliveryMethod.Failed,
                ErrorMessage: ex.Message);
        }
    }
}
