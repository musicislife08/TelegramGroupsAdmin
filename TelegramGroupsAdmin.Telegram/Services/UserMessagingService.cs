using Microsoft.Extensions.Logging;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.Telegram.Services;

public class UserMessagingService : IUserMessagingService
{
    private readonly ITelegramUserRepository _telegramUserRepository;
    private readonly ITelegramBotClientFactory _botClientFactory;
    private readonly ILogger<UserMessagingService> _logger;

    public UserMessagingService(
        ITelegramUserRepository telegramUserRepository,
        ITelegramBotClientFactory botClientFactory,
        ILogger<UserMessagingService> logger)
    {
        _telegramUserRepository = telegramUserRepository;
        _botClientFactory = botClientFactory;
        _logger = logger;
    }

    public async Task<MessageSendResult> SendToUserAsync(
        long userId,
        Chat chat,
        string messageText,
        int? replyToMessageId = null,
        CancellationToken cancellationToken = default)
    {
        var operations = await _botClientFactory.GetOperationsAsync();

        // Get user's DM preference
        var user = await _telegramUserRepository.GetByTelegramIdAsync(userId, cancellationToken);
        var botDmEnabled = user?.BotDmEnabled ?? false;

        // Attempt DM if user has enabled it
        if (botDmEnabled)
        {
            try
            {
                await operations.SendMessageAsync(
                    chatId: userId, // Send to user's private chat
                    text: messageText,
                    parseMode: ParseMode.Markdown,
                    cancellationToken: cancellationToken);

                _logger.LogInformation(
                    "Sent DM to user {User}: {MessagePreview}",
                    user.ToLogInfo(userId),
                    messageText.Length > 50 ? messageText.Substring(0, 50) + "..." : messageText);

                return new MessageSendResult(userId, Success: true, MessageDeliveryMethod.PrivateDm);
            }
            catch (ApiRequestException ex) when (ex.ErrorCode == 403) // Forbidden - user blocked bot
            {
                _logger.LogWarning(
                    "User {User} blocked the bot (Forbidden error). Disabling DM and falling back to chat mention.",
                    user.ToLogDebug(userId));

                // Update database - user blocked the bot
                await _telegramUserRepository.SetBotDmEnabledAsync(userId, enabled: false, cancellationToken);

                // Fall through to chat mention fallback
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to send DM to user {User}. Falling back to chat mention.",
                    user.ToLogDebug(userId));

                // Fall through to chat mention fallback
            }
        }

        // Fallback: Send as chat mention
        return await SendChatMentionAsync(operations, userId, chat, messageText, replyToMessageId, cancellationToken);
    }

    public async Task<List<MessageSendResult>> SendToMultipleUsersAsync(
        List<long> userIds,
        Chat chat,
        string messageText,
        int? replyToMessageId = null,
        CancellationToken cancellationToken = default)
    {
        var operations = await _botClientFactory.GetOperationsAsync();
        var results = new List<MessageSendResult>();
        var failedDmUsers = new List<(long UserId, string Mention)>();

        // Try to send DMs to all users who have it enabled
        foreach (var userId in userIds)
        {
            var user = await _telegramUserRepository.GetByTelegramIdAsync(userId, cancellationToken);
            var botDmEnabled = user?.BotDmEnabled ?? false;

            if (botDmEnabled)
            {
                try
                {
                    await operations.SendMessageAsync(
                        chatId: userId,
                        text: messageText,
                        parseMode: ParseMode.Markdown,
                        cancellationToken: cancellationToken);

                    _logger.LogInformation(
                        "Sent DM to user {User}: {MessagePreview}",
                        user.ToLogInfo(userId),
                        messageText.Length > 50 ? messageText.Substring(0, 50) + "..." : messageText);

                    results.Add(new MessageSendResult(userId, Success: true, MessageDeliveryMethod.PrivateDm));
                }
                catch (ApiRequestException ex) when (ex.ErrorCode == 403)
                {
                    _logger.LogWarning(
                        "User {User} blocked the bot (Forbidden error). Disabling DM and will mention in chat.",
                        user.ToLogDebug(userId));

                    await _telegramUserRepository.SetBotDmEnabledAsync(userId, enabled: false, cancellationToken);

                    var userMention = TelegramDisplayName.FormatMention(user?.FirstName, user?.LastName, user?.Username, userId);
                    failedDmUsers.Add((userId, userMention));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Failed to send DM to user {User}. Will mention in chat.",
                        user.ToLogDebug(userId));

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
        if (failedDmUsers.Any())
        {
            try
            {
                var mentions = string.Join(", ", failedDmUsers.Select(u => u.Mention));
                var chatMessage = $"{mentions}:\n\n{messageText}";

                var sentMessage = await operations.SendMessageAsync(
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
        ITelegramOperations operations,
        long userId,
        Chat chat,
        string messageText,
        int? replyToMessageId,
        CancellationToken cancellationToken)
    {
        // Get user info for mention (fetch before try block so it's available in catch)
        var user = await _telegramUserRepository.GetByTelegramIdAsync(userId, cancellationToken);

        try
        {
            var userMention = TelegramDisplayName.FormatMention(user?.FirstName, user?.LastName, user?.Username, userId);

            // Prefix message with mention
            var chatMessage = $"{userMention}: {messageText}";

            var sentMessage = await operations.SendMessageAsync(
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
