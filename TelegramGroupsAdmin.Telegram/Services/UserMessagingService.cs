using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Identity;

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
    private readonly IUserIdentityService _identityService;
    private readonly IConfigService _configService;
    private readonly ILogger<UserMessagingService> _logger;

    public UserMessagingService(
        ITelegramUserRepository telegramUserRepository,
        IBotDmService dmService,
        IBotMessageService messageService,
        IUserIdentityService identityService,
        IConfigService configService,
        ILogger<UserMessagingService> logger)
    {
        _telegramUserRepository = telegramUserRepository;
        _dmService = dmService;
        _messageService = messageService;
        _identityService = identityService;
        _configService = configService;
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

        var recipient = await _identityService.ResolveAsync(userId, cancellationToken);

        // Try DM via IBotDmService (no fallback - callers decide what happens next)
        var dmResult = await _dmService.SendDmAsync(
            user: recipient,
            message: message,
            fallbackChatId: null,
            cancellationToken: cancellationToken);

        if (dmResult.DmSent)
        {
            _logger.LogInformation(
                "Sent DM to user {User}: {MessagePreview}",
                recipient.ToLogInfo(),
                message.Text.Length > 50 ? message.Text[..50] + "..." : message.Text);

            return true;
        }

        // DM failed (user blocked bot or error)
        _logger.LogDebug("DM to {User} failed", recipient.ToLogDebug());
        return false;
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
        var user = await _identityService.ResolveAsync(userId, cancellationToken);

        try
        {
            var masking = await _configService.GetNameMaskingAsync(chat.Id, cancellationToken);
            var mentionMessage = TelegramMessageBuilder.For(masking)
                .Mention(user)
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
                user.ToLogInfo(),
                chat.ToLogInfo());

            return new MessageSendResult(userId, Success: true, MessageDeliveryMethod.ChatMention);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to send chat mention to user {User} in {Chat}",
                user.ToLogDebug(),
                chat.ToLogDebug());

            return new MessageSendResult(
                userId,
                Success: false,
                MessageDeliveryMethod.Failed,
                ErrorMessage: ex.Message);
        }
    }
}
