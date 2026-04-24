using Microsoft.Extensions.Logging;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.JobPayloads;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using static TelegramGroupsAdmin.Core.BackgroundJobs.DeduplicationKeys;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Bot.Handlers;

namespace TelegramGroupsAdmin.Telegram.Services.Bot;

/// <summary>
/// Centralized DM delivery service with consistent bot_dm_enabled tracking and fallback handling.
/// Scoped service with direct dependency injection.
/// Part of the Bot services layer - can use IBotMessageHandler directly.
/// </summary>
public class BotDmService(
    IBotMessageHandler messageHandler,
    ITelegramUserRepository telegramUserRepository,
    IPendingNotificationsRepository pendingNotificationsRepository,
    IManagedChatsRepository managedChatsRepository,
    IJobScheduler jobScheduler,
    ILogger<BotDmService> logger) : IBotDmService
{

    public async Task<DmDeliveryResult> SendDmAsync(
        long telegramUserId,
        string messageText,
        long? fallbackChatId = null,
        int? autoDeleteSeconds = null,
        CancellationToken cancellationToken = default)
    {
        var user = await telegramUserRepository.GetByTelegramIdAsync(telegramUserId, cancellationToken);

        try
        {
            // Attempt to send DM
            var sentMessage = await messageHandler.SendAsync(
                chatId: telegramUserId,
                text: messageText,
                ct: cancellationToken);

            logger.LogInformation(
                "DM sent successfully to {User}",
                user.ToLogInfo(telegramUserId));

            // Update bot_dm_enabled flag to true (user can receive DMs)
            await telegramUserRepository.EnableBotDmAsync(telegramUserId, cancellationToken);

            return new DmDeliveryResult
            {
                DmSent = true,
                FallbackUsed = false,
                Failed = false,
                MessageId = sentMessage.MessageId
            };
        }
        catch (ApiRequestException ex) when (ex.ErrorCode == 403)
        {
            // User has blocked the bot or hasn't started a DM
            logger.LogWarning(
                "DM blocked for {User} (403 Forbidden){FallbackInfo}",
                user.ToLogDebug(telegramUserId),
                fallbackChatId.HasValue ? $" - falling back to chat {fallbackChatId.Value}" : " - no fallback configured");

            // Update bot_dm_enabled flag to false
            await telegramUserRepository.DisableBotDmAsync(telegramUserId, cancellationToken);

            // If fallback chat is configured, post message there
            if (fallbackChatId.HasValue)
            {
                return await SendFallbackToChatAsync(
                    fallbackChatId.Value,
                    messageText,
                    autoDeleteSeconds,
                    cancellationToken);
            }

            return new DmDeliveryResult
            {
                DmSent = false,
                FallbackUsed = false,
                Failed = true,
                ErrorMessage = "User has not enabled DMs and no fallback chat configured"
            };
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to send DM to {User}",
                user.ToLogDebug(telegramUserId));

            return new DmDeliveryResult
            {
                DmSent = false,
                FallbackUsed = false,
                Failed = true,
                ErrorMessage = ex.Message
            };
        }
    }

    public Task<DmDeliveryResult> SendDmWithQueueAsync(
        long telegramUserId,
        string notificationType,
        string messageText,
        ParseMode parseMode = ParseMode.MarkdownV2,
        CancellationToken cancellationToken = default)
    {
        return TrySendWithQueueAsync(
            telegramUserId,
            notificationType,
            queuedText: messageText,
            sendAction: () => messageHandler.SendAsync(
                chatId: telegramUserId,
                text: messageText,
                parseMode: parseMode,
                ct: cancellationToken),
            cancellationToken,
            logStyle: DmLogStyle.Queue,
            networkErrorAware: true);
    }

    /// <summary>
    /// Send fallback message in chat with optional auto-delete
    /// </summary>
    private async Task<DmDeliveryResult> SendFallbackToChatAsync(
        long chatId,
        string messageText,
        int? autoDeleteSeconds,
        CancellationToken cancellationToken)
    {
        // Fetch chat once for logging (reuse for all logs in this method)
        var chat = await managedChatsRepository.GetByChatIdAsync(chatId, cancellationToken);

        try
        {
            var fallbackMessage = await messageHandler.SendAsync(
                chatId: chatId,
                text: messageText,
                ct: cancellationToken);

            logger.LogInformation(
                "Sent fallback message {MessageId} in {Chat}{DeleteInfo}",
                fallbackMessage.MessageId,
                (chat?.Identity ?? ChatIdentity.FromId(chatId)).ToLogInfo(),
                autoDeleteSeconds.HasValue ? $", will delete in {autoDeleteSeconds.Value} seconds" : "");

            // Schedule auto-delete if requested
            if (autoDeleteSeconds.HasValue && autoDeleteSeconds.Value > 0)
            {
                var deletePayload = new DeleteMessagePayload(
                    chatId,
                    fallbackMessage.MessageId,
                    "dm_fallback"
                );

                await jobScheduler.ScheduleJobAsync(
                    "DeleteMessage",
                    deletePayload,
                    delaySeconds: autoDeleteSeconds.Value,
                    deduplicationKey: None,
                    cancellationToken);
            }

            return new DmDeliveryResult
            {
                DmSent = false,
                FallbackUsed = true,
                Failed = false,
                FallbackMessageId = fallbackMessage.MessageId
            };
        }
        catch (Exception ex)
        {
            // Log network errors cleanly without stack traces
            if (IsNetworkError(ex))
            {
                logger.LogWarning(
                    "Failed to send fallback message in {Chat} - network unavailable",
                    (chat?.Identity ?? ChatIdentity.FromId(chatId)).ToLogDebug());
            }
            else
            {
                logger.LogError(
                    ex,
                    "Failed to send fallback message in {Chat}",
                    (chat?.Identity ?? ChatIdentity.FromId(chatId)).ToLogDebug());
            }

            return new DmDeliveryResult
            {
                DmSent = false,
                FallbackUsed = false,
                Failed = true,
                ErrorMessage = $"Fallback failed: {ex.Message}"
            };
        }
    }

    public Task<DmDeliveryResult> SendDmWithMediaAsync(
        long telegramUserId,
        string notificationType,
        string messageText,
        string? photoPath = null,
        string? videoPath = null,
        CancellationToken cancellationToken = default)
    {
        // Media variants log success internally (messages differ for photo/video/text paths).
        // The helper therefore skips the default success log and delegates entirely to sendAction.
        return TrySendWithQueueAsync(
            telegramUserId,
            notificationType,
            queuedText: messageText,
            sendAction: async () =>
            {
                var user = await telegramUserRepository.GetByTelegramIdAsync(telegramUserId, cancellationToken);
                var hasMedia = !string.IsNullOrWhiteSpace(photoPath) || !string.IsNullOrWhiteSpace(videoPath);

                if (hasMedia)
                {
                    if (!string.IsNullOrWhiteSpace(photoPath) && File.Exists(photoPath))
                    {
                        await using var photoStream = File.OpenRead(photoPath);
                        await messageHandler.SendPhotoAsync(
                            chatId: telegramUserId,
                            photo: InputFile.FromStream(photoStream, Path.GetFileName(photoPath)),
                            caption: messageText,
                            parseMode: ParseMode.MarkdownV2,
                            ct: cancellationToken);

                        logger.LogInformation("DM with photo sent successfully to {User}", user.ToLogInfo(telegramUserId));
                    }
                    else if (!string.IsNullOrWhiteSpace(videoPath) && File.Exists(videoPath))
                    {
                        await using var videoStream = File.OpenRead(videoPath);
                        await messageHandler.SendVideoAsync(
                            chatId: telegramUserId,
                            video: InputFile.FromStream(videoStream, Path.GetFileName(videoPath)),
                            caption: messageText,
                            parseMode: ParseMode.MarkdownV2,
                            ct: cancellationToken);

                        logger.LogInformation("DM with video sent successfully to {User}", user.ToLogInfo(telegramUserId));
                    }
                    else
                    {
                        // Media path provided but file doesn't exist - fallback to text only
                        logger.LogWarning("Media file not found (photo: {PhotoPath}, video: {VideoPath}), sending text-only DM to {User}",
                            photoPath, videoPath, user.ToLogDebug(telegramUserId));

                        await messageHandler.SendAsync(
                            chatId: telegramUserId,
                            text: messageText,
                            parseMode: ParseMode.MarkdownV2,
                            ct: cancellationToken);
                    }
                }
                else
                {
                    // No media - send text only
                    await messageHandler.SendAsync(
                        chatId: telegramUserId,
                        text: messageText,
                        parseMode: ParseMode.MarkdownV2,
                        ct: cancellationToken);

                    logger.LogInformation("DM sent successfully to {User}", user.ToLogInfo(telegramUserId));
                }
            },
            cancellationToken,
            logStyle: DmLogStyle.Media,
            mediaErrorVariant: "media",
            networkErrorAware: false);
    }

    public Task<DmDeliveryResult> SendDmWithMediaAndKeyboardAsync(
        long telegramUserId,
        string notificationType,
        string messageText,
        string? photoPath = null,
        string? videoPath = null,
        InlineKeyboardMarkup? keyboard = null,
        ParseMode parseMode = ParseMode.MarkdownV2,
        CancellationToken cancellationToken = default)
    {
        return TrySendWithQueueAsync(
            telegramUserId,
            notificationType,
            queuedText: messageText,
            sendAction: async () =>
            {
                var user = await telegramUserRepository.GetByTelegramIdAsync(telegramUserId, cancellationToken);

                if (!string.IsNullOrWhiteSpace(photoPath) && File.Exists(photoPath))
                {
                    await using var photoStream = File.OpenRead(photoPath);
                    await messageHandler.SendPhotoAsync(
                        chatId: telegramUserId,
                        photo: InputFile.FromStream(photoStream, Path.GetFileName(photoPath)),
                        caption: messageText,
                        parseMode: parseMode,
                        replyMarkup: keyboard,
                        ct: cancellationToken);

                    logger.LogInformation("DM with photo and keyboard sent successfully to {User}", user.ToLogInfo(telegramUserId));
                }
                else if (!string.IsNullOrWhiteSpace(videoPath) && File.Exists(videoPath))
                {
                    await using var videoStream = File.OpenRead(videoPath);
                    await messageHandler.SendVideoAsync(
                        chatId: telegramUserId,
                        video: InputFile.FromStream(videoStream, Path.GetFileName(videoPath)),
                        caption: messageText,
                        parseMode: parseMode,
                        replyMarkup: keyboard,
                        ct: cancellationToken);

                    logger.LogInformation("DM with video and keyboard sent successfully to {User}", user.ToLogInfo(telegramUserId));
                }
                else
                {
                    // Text-only with keyboard
                    await messageHandler.SendAsync(
                        chatId: telegramUserId,
                        text: messageText,
                        parseMode: parseMode,
                        replyMarkup: keyboard,
                        ct: cancellationToken);

                    logger.LogInformation("DM with keyboard sent successfully to {User}", user.ToLogInfo(telegramUserId));
                }
            },
            cancellationToken,
            logStyle: DmLogStyle.Media,
            mediaErrorVariant: "keyboard",
            networkErrorAware: false);
    }

    public async Task<Message> EditDmTextAsync(
        long dmChatId,
        int messageId,
        string text,
        InlineKeyboardMarkup? replyMarkup = null,
        CancellationToken cancellationToken = default)
    {
        var editedMessage = await messageHandler.EditTextAsync(
            chatId: dmChatId,
            messageId: messageId,
            text: text,
            replyMarkup: replyMarkup,
            ct: cancellationToken);

        logger.LogDebug("Edited DM text message {MessageId} in chat {ChatId}", messageId, dmChatId);

        return editedMessage;
    }

    public async Task<Message> EditDmCaptionAsync(
        long dmChatId,
        int messageId,
        string? caption,
        InlineKeyboardMarkup? replyMarkup = null,
        CancellationToken cancellationToken = default)
    {
        var editedMessage = await messageHandler.EditCaptionAsync(
            chatId: dmChatId,
            messageId: messageId,
            caption: caption,
            replyMarkup: replyMarkup,
            ct: cancellationToken);

        logger.LogDebug("Edited DM caption for message {MessageId} in chat {ChatId}", messageId, dmChatId);

        return editedMessage;
    }

    /// <inheritdoc />
    public async Task DeleteDmMessageAsync(
        long dmChatId,
        int messageId,
        CancellationToken cancellationToken = default)
    {
        await messageHandler.DeleteAsync(dmChatId, messageId, cancellationToken);

        logger.LogDebug("Deleted DM message {MessageId} in chat {ChatId}", messageId, dmChatId);
    }

    /// <inheritdoc />
    public async Task<DmDeliveryResult> SendDmWithKeyboardAsync(
        long telegramUserId,
        string messageText,
        InlineKeyboardMarkup keyboard,
        CancellationToken cancellationToken = default)
    {
        var user = await telegramUserRepository.GetByTelegramIdAsync(telegramUserId, cancellationToken);

        try
        {
            // Send DM with keyboard
            var sentMessage = await messageHandler.SendAsync(
                chatId: telegramUserId,
                text: messageText,
                replyMarkup: keyboard,
                ct: cancellationToken);

            logger.LogDebug(
                "DM with keyboard sent successfully to {User} (MessageId: {MessageId})",
                user.ToLogDebug(telegramUserId),
                sentMessage.MessageId);

            // Update bot_dm_enabled flag to true
            await telegramUserRepository.EnableBotDmAsync(telegramUserId, cancellationToken);

            return new DmDeliveryResult
            {
                DmSent = true,
                FallbackUsed = false,
                Failed = false,
                MessageId = sentMessage.MessageId
            };
        }
        catch (ApiRequestException ex) when (ex.ErrorCode == 403)
        {
            // User has blocked the bot - can't send keyboard messages, no queue fallback
            logger.LogWarning(
                "DM blocked for {User} (403 Forbidden) - cannot send keyboard message",
                user.ToLogDebug(telegramUserId));

            await telegramUserRepository.DisableBotDmAsync(telegramUserId, cancellationToken);

            return new DmDeliveryResult
            {
                DmSent = false,
                FallbackUsed = false,
                Failed = true,
                ErrorMessage = "User has not enabled DMs - keyboard messages cannot be queued"
            };
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to send DM with keyboard to {User}",
                user.ToLogDebug(telegramUserId));

            return new DmDeliveryResult
            {
                DmSent = false,
                FallbackUsed = false,
                Failed = true,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <inheritdoc />
    public Task<DmDeliveryResult> SendDmWithEntitiesAsync(
        long telegramUserId,
        string notificationType,
        string text,
        IReadOnlyList<MessageEntity> entities,
        CancellationToken cancellationToken = default)
    {
        return TrySendWithQueueAsync(
            telegramUserId,
            notificationType,
            queuedText: text,
            sendAction: () => messageHandler.SendAsync(
                chatId: telegramUserId,
                text: text,
                parseMode: null,
                entities: entities,
                ct: cancellationToken),
            cancellationToken,
            logStyle: DmLogStyle.Queue,
            networkErrorAware: true);
    }

    /// <inheritdoc />
    public Task<DmDeliveryResult> SendDmWithMediaAndKeyboardEntitiesAsync(
        long telegramUserId,
        string notificationType,
        string text,
        IReadOnlyList<MessageEntity> entities,
        string? photoPath = null,
        string? videoPath = null,
        InlineKeyboardMarkup? keyboard = null,
        CancellationToken cancellationToken = default)
    {
        return TrySendWithQueueAsync(
            telegramUserId,
            notificationType,
            queuedText: text,
            sendAction: async () =>
            {
                var user = await telegramUserRepository.GetByTelegramIdAsync(telegramUserId, cancellationToken);

                if (!string.IsNullOrWhiteSpace(photoPath) && File.Exists(photoPath))
                {
                    await using var photoStream = File.OpenRead(photoPath);
                    await messageHandler.SendPhotoAsync(
                        chatId: telegramUserId,
                        photo: InputFile.FromStream(photoStream, Path.GetFileName(photoPath)),
                        caption: text,
                        parseMode: null,
                        replyMarkup: keyboard,
                        captionEntities: entities,
                        ct: cancellationToken);

                    logger.LogInformation(
                        "DM with photo/entities/keyboard sent successfully to {User}",
                        user.ToLogInfo(telegramUserId));
                }
                else if (!string.IsNullOrWhiteSpace(videoPath) && File.Exists(videoPath))
                {
                    await using var videoStream = File.OpenRead(videoPath);
                    await messageHandler.SendVideoAsync(
                        chatId: telegramUserId,
                        video: InputFile.FromStream(videoStream, Path.GetFileName(videoPath)),
                        caption: text,
                        parseMode: null,
                        replyMarkup: keyboard,
                        captionEntities: entities,
                        ct: cancellationToken);

                    logger.LogInformation(
                        "DM with video/entities/keyboard sent successfully to {User}",
                        user.ToLogInfo(telegramUserId));
                }
                else
                {
                    await messageHandler.SendAsync(
                        chatId: telegramUserId,
                        text: text,
                        parseMode: null,
                        replyMarkup: keyboard,
                        entities: entities,
                        ct: cancellationToken);

                    logger.LogInformation(
                        "DM with entities/keyboard sent successfully to {User}",
                        user.ToLogInfo(telegramUserId));
                }
            },
            cancellationToken,
            logStyle: DmLogStyle.Media,
            mediaErrorVariant: "entities/media/keyboard",
            networkErrorAware: false);
    }

    /// <summary>
    /// Log style for <see cref="TrySendWithQueueAsync"/>.
    /// Queue-style matches the parse-mode/entities text methods (info success + warning on 403 with
    /// "queueing" verbiage + network-aware error). Media-style matches the media/keyboard variants
    /// (success logged internally by the sendAction + info on 403 with "blocked bot DMs" verbiage
    /// + custom error wording).
    /// </summary>
    private enum DmLogStyle
    {
        Queue,
        Media
    }

    /// <summary>
    /// Shared try/catch/403/queue skeleton for DM send methods.
    /// Handles user lookup, success flag flip, 403 → queue, and generic error logging so each
    /// public overload only has to supply the actual send action.
    /// </summary>
    /// <param name="sendAction">
    /// Delegate that performs the actual Telegram API call. For Media-style callers, this delegate
    /// is also responsible for logging the success message (since the message differs per path —
    /// photo vs video vs text-only). For Queue-style callers, the helper logs a single generic
    /// success message.
    /// </param>
    /// <param name="mediaErrorVariant">
    /// Required when <paramref name="logStyle"/> is Media — the wording inserted into the generic
    /// error log, e.g. "media" → "Failed to send DM with media to {User}".
    /// </param>
    private async Task<DmDeliveryResult> TrySendWithQueueAsync(
        long telegramUserId,
        string notificationType,
        string queuedText,
        Func<Task> sendAction,
        CancellationToken cancellationToken,
        DmLogStyle logStyle,
        string? mediaErrorVariant = null,
        bool networkErrorAware = false)
    {
        var user = await telegramUserRepository.GetByTelegramIdAsync(telegramUserId, cancellationToken);

        try
        {
            await sendAction();

            if (logStyle == DmLogStyle.Queue)
            {
                logger.LogInformation(
                    "DM sent successfully to {User} (notification type: {NotificationType})",
                    user.ToLogInfo(telegramUserId),
                    notificationType);
            }
            // Media-style logs success inside sendAction (different message per photo/video/text path).

            await telegramUserRepository.EnableBotDmAsync(telegramUserId, cancellationToken);

            return new DmDeliveryResult
            {
                DmSent = true,
                FallbackUsed = false,
                Failed = false
            };
        }
        catch (ApiRequestException ex) when (ex.ErrorCode == 403)
        {
            if (logStyle == DmLogStyle.Queue)
            {
                logger.LogWarning(
                    "DM blocked for {User} - queueing {NotificationType} notification for later delivery",
                    user.ToLogDebug(telegramUserId),
                    notificationType);
            }
            else
            {
                logger.LogInformation(
                    "{User} has blocked bot DMs (403), queuing notification",
                    user.ToLogInfo(telegramUserId));
            }

            await telegramUserRepository.DisableBotDmAsync(telegramUserId, cancellationToken);

            await pendingNotificationsRepository.AddPendingNotificationAsync(
                telegramUserId,
                notificationType,
                queuedText,
                cancellationToken: cancellationToken);

            return new DmDeliveryResult
            {
                DmSent = false,
                FallbackUsed = false,
                Failed = true,
                ErrorMessage = logStyle == DmLogStyle.Queue
                    ? "User has not enabled DMs - notification queued for later delivery"
                    : "User has blocked bot DMs - notification queued for later delivery"
            };
        }
        catch (Exception ex)
        {
            if (networkErrorAware && IsNetworkError(ex))
            {
                logger.LogWarning(
                    "Failed to send DM to {User} - network unavailable (notification type: {NotificationType})",
                    user.ToLogDebug(telegramUserId),
                    notificationType);
            }
            else if (logStyle == DmLogStyle.Queue)
            {
                logger.LogError(
                    ex,
                    "Failed to send DM to {User} (notification type: {NotificationType})",
                    user.ToLogDebug(telegramUserId),
                    notificationType);
            }
            else
            {
                // Preserve original literal error templates (one per media variant) so structured
                // log consumers see the same template strings as before the refactor.
                switch (mediaErrorVariant)
                {
                    case "media":
                        logger.LogError(ex, "Failed to send DM with media to {User}", user.ToLogDebug(telegramUserId));
                        break;
                    case "keyboard":
                        logger.LogError(ex, "Failed to send DM with keyboard to {User}", user.ToLogDebug(telegramUserId));
                        break;
                    case "entities/media/keyboard":
                        logger.LogError(ex, "Failed to send DM with entities/media/keyboard to {User}", user.ToLogDebug(telegramUserId));
                        break;
                    default:
                        logger.LogError(ex, "Failed to send DM to {User}", user.ToLogDebug(telegramUserId));
                        break;
                }
            }

            return new DmDeliveryResult
            {
                DmSent = false,
                FallbackUsed = false,
                Failed = true,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// Check if exception is a network error (DNS, connection timeout, etc.)
    /// </summary>
    private static bool IsNetworkError(Exception ex)
    {
        // Check for HttpRequestException or SocketException (network errors)
        return ex is HttpRequestException
               || ex.InnerException is HttpRequestException
               || ex.InnerException?.InnerException is System.Net.Sockets.SocketException
               || ex is System.Net.Sockets.SocketException;
    }
}
