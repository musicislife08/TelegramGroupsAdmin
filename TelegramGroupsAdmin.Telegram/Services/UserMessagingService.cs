using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.Telegram.Services;

public class UserMessagingService : IUserMessagingService
{
    private readonly ITelegramUserRepository _telegramUserRepository;
    private readonly ILogger<UserMessagingService> _logger;

    public UserMessagingService(
        ITelegramUserRepository telegramUserRepository,
        ILogger<UserMessagingService> logger)
    {
        _telegramUserRepository = telegramUserRepository;
        _logger = logger;
    }

    public async Task<MessageSendResult> SendToUserAsync(
        ITelegramBotClient botClient,
        long userId,
        long chatId,
        string messageText,
        int? replyToMessageId = null,
        CancellationToken cancellationToken = default)
    {
        // Get user's DM preference
        var user = await _telegramUserRepository.GetByTelegramIdAsync(userId, cancellationToken);
        var botDmEnabled = user?.BotDmEnabled ?? false;

        // Attempt DM if user has enabled it
        if (botDmEnabled)
        {
            try
            {
                await botClient.SendMessage(
                    chatId: userId, // Send to user's private chat
                    text: messageText,
                    parseMode: ParseMode.Markdown,
                    cancellationToken: cancellationToken);

                _logger.LogInformation(
                    "Sent DM to user {UserId}: {MessagePreview}",
                    userId,
                    messageText.Length > 50 ? messageText.Substring(0, 50) + "..." : messageText);

                return new MessageSendResult(userId, Success: true, MessageDeliveryMethod.PrivateDm);
            }
            catch (ApiRequestException ex) when (ex.ErrorCode == 403) // Forbidden - user blocked bot
            {
                _logger.LogWarning(
                    "User {UserId} blocked the bot (Forbidden error). Disabling DM and falling back to chat mention.",
                    userId);

                // Update database - user blocked the bot
                await _telegramUserRepository.SetBotDmEnabledAsync(userId, enabled: false, cancellationToken);

                // Fall through to chat mention fallback
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to send DM to user {UserId}. Falling back to chat mention.",
                    userId);

                // Fall through to chat mention fallback
            }
        }

        // Fallback: Send as chat mention
        return await SendChatMentionAsync(botClient, userId, chatId, messageText, replyToMessageId, cancellationToken);
    }

    public async Task<List<MessageSendResult>> SendToMultipleUsersAsync(
        ITelegramBotClient botClient,
        List<long> userIds,
        long chatId,
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
                try
                {
                    await botClient.SendMessage(
                        chatId: userId,
                        text: messageText,
                        parseMode: ParseMode.Markdown,
                        cancellationToken: cancellationToken);

                    _logger.LogInformation(
                        "Sent DM to user {UserId}: {MessagePreview}",
                        userId,
                        messageText.Length > 50 ? messageText.Substring(0, 50) + "..." : messageText);

                    results.Add(new MessageSendResult(userId, Success: true, MessageDeliveryMethod.PrivateDm));
                }
                catch (ApiRequestException ex) when (ex.ErrorCode == 403)
                {
                    _logger.LogWarning(
                        "User {UserId} blocked the bot (Forbidden error). Disabling DM and will mention in chat.",
                        userId);

                    await _telegramUserRepository.SetBotDmEnabledAsync(userId, enabled: false, cancellationToken);

                    var userMention = TelegramDisplayName.FormatMention(user?.FirstName, user?.LastName, user?.Username, userId);
                    failedDmUsers.Add((userId, userMention));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Failed to send DM to user {UserId}. Will mention in chat.",
                        userId);

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

                var sentMessage = await botClient.SendMessage(
                    chatId: chatId,
                    text: chatMessage,
                    parseMode: ParseMode.Markdown,
                    replyParameters: replyToMessageId.HasValue
                        ? new ReplyParameters { MessageId = replyToMessageId.Value }
                        : null,
                    cancellationToken: cancellationToken);

                _logger.LogInformation(
                    "Sent batched chat mention to {UserCount} users in chat {ChatId}",
                    failedDmUsers.Count,
                    chatId);

                // Add success result for all users in the batch
                foreach (var (userId, _) in failedDmUsers)
                {
                    results.Add(new MessageSendResult(userId, Success: true, MessageDeliveryMethod.ChatMention));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to send batched chat mention to {UserCount} users in chat {ChatId}",
                    failedDmUsers.Count,
                    chatId);

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
        ITelegramBotClient botClient,
        long userId,
        long chatId,
        string messageText,
        int? replyToMessageId,
        CancellationToken cancellationToken)
    {
        try
        {
            // Get user info for mention
            var user = await _telegramUserRepository.GetByTelegramIdAsync(userId, cancellationToken);
            var userMention = TelegramDisplayName.FormatMention(user?.FirstName, user?.LastName, user?.Username, userId);

            // Prefix message with mention
            var chatMessage = $"{userMention}: {messageText}";

            var sentMessage = await botClient.SendMessage(
                chatId: chatId,
                text: chatMessage,
                parseMode: ParseMode.Markdown,
                replyParameters: replyToMessageId.HasValue
                    ? new ReplyParameters { MessageId = replyToMessageId.Value }
                    : null,
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Sent chat mention to user {UserId} in chat {ChatId}",
                userId,
                chatId);

            return new MessageSendResult(userId, Success: true, MessageDeliveryMethod.ChatMention);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to send chat mention to user {UserId} in chat {ChatId}",
                userId,
                chatId);

            return new MessageSendResult(
                userId,
                Success: false,
                MessageDeliveryMethod.Failed,
                ErrorMessage: ex.Message);
        }
    }
}
