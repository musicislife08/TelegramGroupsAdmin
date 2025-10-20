using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Service for sending messages to users with DM preference handling and fallback.
/// Attempts private DM first (if user enabled), falls back to chat mentions on failure.
/// </summary>
public interface IUserMessagingService
{
    /// <summary>
    /// Send a message to a user, attempting DM first if enabled, falling back to chat mention.
    /// </summary>
    /// <param name="botClient">Telegram bot client</param>
    /// <param name="userId">Target user's Telegram ID</param>
    /// <param name="chatId">Chat ID for fallback mention (required)</param>
    /// <param name="messageText">Message to send</param>
    /// <param name="replyToMessageId">Optional message ID to reply to in chat fallback</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if message was sent successfully (either DM or fallback), false if both failed</returns>
    Task<MessageSendResult> SendToUserAsync(
        ITelegramBotClient botClient,
        long userId,
        long chatId,
        string messageText,
        int? replyToMessageId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Send a notification to multiple users (e.g., all admins in a chat).
    /// Each user gets DM if available, otherwise fallback to single chat mention.
    /// </summary>
    Task<List<MessageSendResult>> SendToMultipleUsersAsync(
        ITelegramBotClient botClient,
        List<long> userIds,
        long chatId,
        string messageText,
        int? replyToMessageId = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a message send attempt
/// </summary>
public record MessageSendResult(
    long UserId,
    bool Success,
    MessageDeliveryMethod DeliveryMethod,
    string? ErrorMessage = null);

/// <summary>
/// Message delivery method classification
/// </summary>
public enum MessageDeliveryMethod
{
    /// <summary>Message sent via private DM</summary>
    PrivateDm,

    /// <summary>Message sent as chat mention (fallback)</summary>
    ChatMention,

    /// <summary>Both DM and fallback failed</summary>
    Failed
}

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

        foreach (var userId in userIds)
        {
            var result = await SendToUserAsync(botClient, userId, chatId, messageText, replyToMessageId, cancellationToken);
            results.Add(result);
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
            var userMention = user?.Username != null
                ? $"@{user.Username}"
                : user?.FirstName ?? $"User {userId}";

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
