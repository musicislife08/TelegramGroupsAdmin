using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Bot;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Service for sending messages to specific users.
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
        TelegramMessage message,
        int? replyToMessageId = null,
        CancellationToken cancellationToken = default)
    {
        if (await TrySendDmAsync(userId, message, cancellationToken))
        {
            return new MessageSendResult(userId, Success: true, MessageDeliveryMethod.PrivateDm);
        }

        // Fallback: Send as chat mention
        return await SendChatMentionAsync(userId, chat, message, replyToMessageId, cancellationToken);
    }

    public async Task<MessageSendResult> SendDmOnlyAsync(
        long userId,
        TelegramMessage message,
        CancellationToken cancellationToken = default)
    {
        if (await TrySendDmAsync(userId, message, cancellationToken))
        {
            return new MessageSendResult(userId, Success: true, MessageDeliveryMethod.PrivateDm);
        }

        // No fallback: the recipient can't read a chat mention, so nothing is sent
        return new MessageSendResult(
            userId,
            Success: false,
            MessageDeliveryMethod.Failed,
            ErrorMessage: "DM unavailable and no chat-mention fallback for this message");
    }

    /// <summary>
    /// Attempt a DM to the user, honouring their stored DM preference.
    /// Returns true only when the DM was actually delivered.
    /// </summary>
    private async Task<bool> TrySendDmAsync(
        long userId,
        TelegramMessage message,
        CancellationToken cancellationToken)
    {
        // Get user's DM preference (optimization: skip DM attempt if user blocked bot)
        var user = await _telegramUserRepository.GetByTelegramIdAsync(userId, cancellationToken);
        if (user?.BotDmEnabled is not true)
        {
            return false;
        }

        // Try DM via IBotDmService (no fallback - callers decide what happens next)
        var dmResult = await _dmService.SendDmAsync(
            user: UserIdentity.From(user),
            message: message,
            fallbackChatId: null,
            cancellationToken: cancellationToken);

        if (dmResult.DmSent)
        {
            _logger.LogInformation(
                "Sent DM to user {User}: {MessagePreview}",
                user.ToLogInfo(userId),
                message.Text.Length > 50 ? message.Text[..50] + "..." : message.Text);

            return true;
        }

        // DM failed (user blocked bot or error)
        _logger.LogDebug("DM to {User} failed", user.ToLogDebug(userId));
        return false;
    }

    public async Task<List<MessageSendResult>> SendToMultipleUsersAsync(
        List<long> userIds,
        Chat chat,
        TelegramMessage message,
        int? replyToMessageId = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<MessageSendResult>();
        var failedDmUsers = new List<(long UserId, UserIdentity User)>();

        // Try to send DMs to all users who have it enabled
        foreach (var userId in userIds)
        {
            var user = await _telegramUserRepository.GetByTelegramIdAsync(userId, cancellationToken);
            var botDmEnabled = user?.BotDmEnabled ?? false;

            if (botDmEnabled)
            {
                // Try DM via IBotDmService
                var dmResult = await _dmService.SendDmAsync(
                    user: user != null ? UserIdentity.From(user) : UserIdentity.FromId(userId),
                    message: message,
                    fallbackChatId: null,
                    cancellationToken: cancellationToken);

                if (dmResult.DmSent)
                {
                    _logger.LogInformation(
                        "Sent DM to user {User}: {MessagePreview}",
                        user.ToLogInfo(userId),
                        message.Text.Length > 50 ? message.Text[..50] + "..." : message.Text);

                    results.Add(new MessageSendResult(userId, Success: true, MessageDeliveryMethod.PrivateDm));
                }
                else
                {
                    // DM failed - add to batch mention list
                    failedDmUsers.Add((userId, new UserIdentity(userId, user?.FirstName, user?.LastName, user?.Username)));
                }
            }
            else
            {
                // User doesn't have DM enabled, add to batch mention list
                failedDmUsers.Add((userId, new UserIdentity(userId, user?.FirstName, user?.LastName, user?.Username)));
            }
        }

        // If any users need chat mentions, send ONE message with all mentions
        if (failedDmUsers.Count > 0)
        {
            try
            {
                var builder = new TelegramMessageBuilder();
                for (var i = 0; i < failedDmUsers.Count; i++)
                {
                    if (i > 0) builder.Text(", ");
                    builder.Mention(failedDmUsers[i].User);
                }
                builder.Text(":").LineBreak().LineBreak().Append(message);

                await _messageService.SendAndSaveMessageAsync(
                    chatId: chat.Id,
                    message: builder.Build(),
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
        TelegramMessage message,
        int? replyToMessageId,
        CancellationToken cancellationToken)
    {
        // Get user info for mention
        var user = await _telegramUserRepository.GetByTelegramIdAsync(userId, cancellationToken);

        try
        {
            var mentionMessage = new TelegramMessageBuilder()
                .Mention(new UserIdentity(userId, user?.FirstName, user?.LastName, user?.Username))
                .Text(": ")
                .Append(message)
                .Build();

            await _messageService.SendAndSaveMessageAsync(
                chatId: chat.Id,
                message: mentionMessage,
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
