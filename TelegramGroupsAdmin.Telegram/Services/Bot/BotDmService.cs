using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Core.JobPayloads;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using static TelegramGroupsAdmin.Core.BackgroundJobs.DeduplicationKeys;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Bot.Handlers;

namespace TelegramGroupsAdmin.Telegram.Services.Bot;

/// <summary>
/// Centralized DM delivery service with consistent bot_dm_enabled tracking and fallback handling.
/// Singleton service that creates scopes internally for handler and repository access.
/// Part of the Bot services layer - can use IBotMessageHandler directly.
/// </summary>
public class BotDmService : IBotDmService
{
    private readonly ILogger<BotDmService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IJobScheduler _jobScheduler;

    public BotDmService(
        ILogger<BotDmService> logger,
        IServiceProvider serviceProvider,
        IJobScheduler jobScheduler)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _jobScheduler = jobScheduler;
    }

    public async Task<DmDeliveryResult> SendDmAsync(
        long telegramUserId,
        string messageText,
        long? fallbackChatId = null,
        int? autoDeleteSeconds = null,
        CancellationToken cancellationToken = default)
    {
        // Create scope for handler and repository access (singleton needs scoped services)
        using var scope = _serviceProvider.CreateScope();
        var messageHandler = scope.ServiceProvider.GetRequiredService<IBotMessageHandler>();
        var telegramUserRepository = scope.ServiceProvider.GetRequiredService<ITelegramUserRepository>();
        var user = await telegramUserRepository.GetByTelegramIdAsync(telegramUserId, cancellationToken);

        try
        {
            // Attempt to send DM
            var sentMessage = await messageHandler.SendAsync(
                chatId: telegramUserId,
                text: messageText,
                ct: cancellationToken);

            _logger.LogInformation(
                "DM sent successfully to {User}",
                user.ToLogInfo(telegramUserId));

            // Update bot_dm_enabled flag to true (user can receive DMs)
            await telegramUserRepository.SetBotDmEnabledAsync(telegramUserId, true, cancellationToken);

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
            _logger.LogWarning(
                "DM blocked for {User} (403 Forbidden){FallbackInfo}",
                user.ToLogDebug(telegramUserId),
                fallbackChatId.HasValue ? $" - falling back to chat {fallbackChatId.Value}" : " - no fallback configured");

            // Update bot_dm_enabled flag to false
            await telegramUserRepository.SetBotDmEnabledAsync(telegramUserId, false, cancellationToken);

            // If fallback chat is configured, post message there
            if (fallbackChatId.HasValue)
            {
                return await SendFallbackToChatAsync(
                    messageHandler,
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
            _logger.LogError(
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

    public async Task<DmDeliveryResult> SendDmWithQueueAsync(
        long telegramUserId,
        string notificationType,
        string messageText,
        CancellationToken cancellationToken = default)
    {
        // Create scope for handler and repository access (singleton needs scoped services)
        using var scope = _serviceProvider.CreateScope();
        var messageHandler = scope.ServiceProvider.GetRequiredService<IBotMessageHandler>();
        var telegramUserRepository = scope.ServiceProvider.GetRequiredService<ITelegramUserRepository>();
        var pendingNotificationsRepository = scope.ServiceProvider.GetRequiredService<IPendingNotificationsRepository>();
        var user = await telegramUserRepository.GetByTelegramIdAsync(telegramUserId, cancellationToken);

        try
        {
            // Attempt to send DM
            await messageHandler.SendAsync(
                chatId: telegramUserId,
                text: messageText,
                ct: cancellationToken);

            _logger.LogInformation(
                "DM sent successfully to {User} (notification type: {NotificationType})",
                user.ToLogInfo(telegramUserId),
                notificationType);

            // Update bot_dm_enabled flag to true (user can receive DMs)
            await telegramUserRepository.SetBotDmEnabledAsync(telegramUserId, true, cancellationToken);

            return new DmDeliveryResult
            {
                DmSent = true,
                FallbackUsed = false,
                Failed = false
            };
        }
        catch (ApiRequestException ex) when (ex.ErrorCode == 403)
        {
            // User has blocked the bot or hasn't started a DM - queue for later
            _logger.LogWarning(
                "DM blocked for {User} - queueing {NotificationType} notification for later delivery",
                user.ToLogDebug(telegramUserId),
                notificationType);

            // Update bot_dm_enabled flag to false and queue notification
            await telegramUserRepository.SetBotDmEnabledAsync(telegramUserId, false, cancellationToken);

            // Queue notification for later delivery
            await pendingNotificationsRepository.AddPendingNotificationAsync(
                telegramUserId,
                notificationType,
                messageText,
                cancellationToken: cancellationToken);

            return new DmDeliveryResult
            {
                DmSent = false,
                FallbackUsed = false,
                Failed = true,
                ErrorMessage = "User has not enabled DMs - notification queued for later delivery"
            };
        }
        catch (Exception ex)
        {
            // Log network errors cleanly without stack traces
            if (IsNetworkError(ex))
            {
                _logger.LogWarning(
                    "Failed to send DM to {User} - network unavailable (notification type: {NotificationType})",
                    user.ToLogDebug(telegramUserId),
                    notificationType);
            }
            else
            {
                _logger.LogError(
                    ex,
                    "Failed to send DM to {User} (notification type: {NotificationType})",
                    user.ToLogDebug(telegramUserId),
                    notificationType);
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
    /// Send fallback message in chat with optional auto-delete
    /// </summary>
    private async Task<DmDeliveryResult> SendFallbackToChatAsync(
        IBotMessageHandler messageHandler,
        long chatId,
        string messageText,
        int? autoDeleteSeconds,
        CancellationToken cancellationToken)
    {
        // Fetch chat once for logging (reuse for all logs in this method)
        using var scope = _serviceProvider.CreateScope();
        var managedChatsRepo = scope.ServiceProvider.GetRequiredService<IManagedChatsRepository>();
        var chat = await managedChatsRepo.GetByChatIdAsync(chatId, cancellationToken);

        try
        {
            var fallbackMessage = await messageHandler.SendAsync(
                chatId: chatId,
                text: messageText,
                ct: cancellationToken);

            _logger.LogInformation(
                "Sent fallback message {MessageId} in {Chat}{DeleteInfo}",
                fallbackMessage.MessageId,
                chat.ToLogInfo(chatId),
                autoDeleteSeconds.HasValue ? $", will delete in {autoDeleteSeconds.Value} seconds" : "");

            // Schedule auto-delete if requested
            if (autoDeleteSeconds.HasValue && autoDeleteSeconds.Value > 0)
            {
                var deletePayload = new DeleteMessagePayload(
                    chatId,
                    fallbackMessage.MessageId,
                    "dm_fallback"
                );

                await _jobScheduler.ScheduleJobAsync(
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
                _logger.LogWarning(
                    "Failed to send fallback message in {Chat} - network unavailable",
                    chat.ToLogDebug(chatId));
            }
            else
            {
                _logger.LogError(
                    ex,
                    "Failed to send fallback message in {Chat}",
                    chat.ToLogDebug(chatId));
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

    public async Task<DmDeliveryResult> SendDmWithMediaAsync(
        long telegramUserId,
        string notificationType,
        string messageText,
        string? photoPath = null,
        string? videoPath = null,
        CancellationToken cancellationToken = default)
    {
        // Create scope for handler and repository access (singleton needs scoped services)
        using var scope = _serviceProvider.CreateScope();
        var messageHandler = scope.ServiceProvider.GetRequiredService<IBotMessageHandler>();
        var telegramUserRepo = scope.ServiceProvider.GetRequiredService<ITelegramUserRepository>();
        var notificationRepo = scope.ServiceProvider.GetRequiredService<IPendingNotificationsRepository>();
        var user = await telegramUserRepo.GetByTelegramIdAsync(telegramUserId, cancellationToken);

        try
        {
            // Determine if we're sending with media
            var hasMedia = !string.IsNullOrWhiteSpace(photoPath) || !string.IsNullOrWhiteSpace(videoPath);

            if (hasMedia)
            {
                // Send with media (photo or video)
                if (!string.IsNullOrWhiteSpace(photoPath) && File.Exists(photoPath))
                {
                    // Send photo with caption
                    await using var photoStream = File.OpenRead(photoPath);
                    await messageHandler.SendPhotoAsync(
                        chatId: telegramUserId,
                        photo: InputFile.FromStream(photoStream, Path.GetFileName(photoPath)),
                        caption: messageText,
                        parseMode: ParseMode.MarkdownV2,
                        ct: cancellationToken);

                    _logger.LogInformation("DM with photo sent successfully to {User}", user.ToLogInfo(telegramUserId));
                }
                else if (!string.IsNullOrWhiteSpace(videoPath) && File.Exists(videoPath))
                {
                    // Send video with caption
                    await using var videoStream = File.OpenRead(videoPath);
                    await messageHandler.SendVideoAsync(
                        chatId: telegramUserId,
                        video: InputFile.FromStream(videoStream, Path.GetFileName(videoPath)),
                        caption: messageText,
                        parseMode: ParseMode.MarkdownV2,
                        ct: cancellationToken);

                    _logger.LogInformation("DM with video sent successfully to {User}", user.ToLogInfo(telegramUserId));
                }
                else
                {
                    // Media path provided but file doesn't exist - fallback to text only
                    _logger.LogWarning("Media file not found (photo: {PhotoPath}, video: {VideoPath}), sending text-only DM to {User}",
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

                _logger.LogInformation("DM sent successfully to {User}", user.ToLogInfo(telegramUserId));
            }

            // Update bot_dm_enabled flag to true
            await telegramUserRepo.SetBotDmEnabledAsync(telegramUserId, true, cancellationToken);

            return new DmDeliveryResult
            {
                DmSent = true,
                FallbackUsed = false,
                Failed = false
            };
        }
        catch (ApiRequestException ex) when (ex.ErrorCode == 403)
        {
            // User has blocked the bot - update flag and queue notification
            _logger.LogInformation("{User} has blocked bot DMs (403), queuing notification", user.ToLogInfo(telegramUserId));

            await telegramUserRepo.SetBotDmEnabledAsync(telegramUserId, false, cancellationToken);

            // Queue notification for later delivery
            await notificationRepo.AddPendingNotificationAsync(telegramUserId, notificationType, messageText, cancellationToken: cancellationToken);

            return new DmDeliveryResult
            {
                DmSent = false,
                FallbackUsed = false,
                Failed = true,
                ErrorMessage = "User has blocked bot DMs - notification queued for later delivery"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send DM with media to {User}", user.ToLogDebug(telegramUserId));
            return new DmDeliveryResult
            {
                DmSent = false,
                FallbackUsed = false,
                Failed = true,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<DmDeliveryResult> SendDmWithMediaAndKeyboardAsync(
        long telegramUserId,
        string notificationType,
        string messageText,
        string? photoPath = null,
        global::Telegram.Bot.Types.ReplyMarkups.InlineKeyboardMarkup? keyboard = null,
        CancellationToken cancellationToken = default)
    {
        // Create scope for handler and repository access (singleton needs scoped services)
        using var scope = _serviceProvider.CreateScope();
        var messageHandler = scope.ServiceProvider.GetRequiredService<IBotMessageHandler>();
        var telegramUserRepo = scope.ServiceProvider.GetRequiredService<ITelegramUserRepository>();
        var notificationRepo = scope.ServiceProvider.GetRequiredService<IPendingNotificationsRepository>();
        var user = await telegramUserRepo.GetByTelegramIdAsync(telegramUserId, cancellationToken);

        try
        {
            // Send with photo if available
            if (!string.IsNullOrWhiteSpace(photoPath) && File.Exists(photoPath))
            {
                await using var photoStream = File.OpenRead(photoPath);
                await messageHandler.SendPhotoAsync(
                    chatId: telegramUserId,
                    photo: InputFile.FromStream(photoStream, Path.GetFileName(photoPath)),
                    caption: messageText,
                    parseMode: ParseMode.MarkdownV2,
                    replyMarkup: keyboard,
                    ct: cancellationToken);

                _logger.LogInformation("DM with photo and keyboard sent successfully to {User}", user.ToLogInfo(telegramUserId));
            }
            else
            {
                // Text-only with keyboard
                await messageHandler.SendAsync(
                    chatId: telegramUserId,
                    text: messageText,
                    parseMode: ParseMode.MarkdownV2,
                    replyMarkup: keyboard,
                    ct: cancellationToken);

                _logger.LogInformation("DM with keyboard sent successfully to {User}", user.ToLogInfo(telegramUserId));
            }

            // Update bot_dm_enabled flag to true
            await telegramUserRepo.SetBotDmEnabledAsync(telegramUserId, true, cancellationToken);

            return new DmDeliveryResult
            {
                DmSent = true,
                FallbackUsed = false,
                Failed = false
            };
        }
        catch (ApiRequestException ex) when (ex.ErrorCode == 403)
        {
            // User has blocked the bot - update flag and queue notification (without buttons)
            _logger.LogInformation("{User} has blocked bot DMs (403), queuing notification", user.ToLogInfo(telegramUserId));

            await telegramUserRepo.SetBotDmEnabledAsync(telegramUserId, false, cancellationToken);

            // Queue notification for later delivery (text only, no buttons)
            await notificationRepo.AddPendingNotificationAsync(telegramUserId, notificationType, messageText, cancellationToken: cancellationToken);

            return new DmDeliveryResult
            {
                DmSent = false,
                FallbackUsed = false,
                Failed = true,
                ErrorMessage = "User has blocked bot DMs - notification queued for later delivery"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send DM with keyboard to {User}", user.ToLogDebug(telegramUserId));
            return new DmDeliveryResult
            {
                DmSent = false,
                FallbackUsed = false,
                Failed = true,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<Message> EditDmTextAsync(
        long dmChatId,
        int messageId,
        string text,
        global::Telegram.Bot.Types.ReplyMarkups.InlineKeyboardMarkup? replyMarkup = null,
        CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var messageHandler = scope.ServiceProvider.GetRequiredService<IBotMessageHandler>();

        var editedMessage = await messageHandler.EditTextAsync(
            chatId: dmChatId,
            messageId: messageId,
            text: text,
            replyMarkup: replyMarkup,
            ct: cancellationToken);

        _logger.LogDebug("Edited DM text message {MessageId} in chat {ChatId}", messageId, dmChatId);

        return editedMessage;
    }

    public async Task<Message> EditDmCaptionAsync(
        long dmChatId,
        int messageId,
        string? caption,
        global::Telegram.Bot.Types.ReplyMarkups.InlineKeyboardMarkup? replyMarkup = null,
        CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var messageHandler = scope.ServiceProvider.GetRequiredService<IBotMessageHandler>();

        var editedMessage = await messageHandler.EditCaptionAsync(
            chatId: dmChatId,
            messageId: messageId,
            caption: caption,
            replyMarkup: replyMarkup,
            ct: cancellationToken);

        _logger.LogDebug("Edited DM caption for message {MessageId} in chat {ChatId}", messageId, dmChatId);

        return editedMessage;
    }

    /// <inheritdoc />
    public async Task DeleteDmMessageAsync(
        long dmChatId,
        int messageId,
        CancellationToken cancellationToken = default)
    {
        // Create scope for handler access (singleton needs scoped services)
        using var scope = _serviceProvider.CreateScope();
        var messageHandler = scope.ServiceProvider.GetRequiredService<IBotMessageHandler>();

        await messageHandler.DeleteAsync(dmChatId, messageId, cancellationToken);

        _logger.LogDebug("Deleted DM message {MessageId} in chat {ChatId}", messageId, dmChatId);
    }

    /// <inheritdoc />
    public async Task<DmDeliveryResult> SendDmWithKeyboardAsync(
        long telegramUserId,
        string messageText,
        global::Telegram.Bot.Types.ReplyMarkups.InlineKeyboardMarkup keyboard,
        CancellationToken cancellationToken = default)
    {
        // Create scope for handler and repository access (singleton needs scoped services)
        using var scope = _serviceProvider.CreateScope();
        var messageHandler = scope.ServiceProvider.GetRequiredService<IBotMessageHandler>();
        var telegramUserRepo = scope.ServiceProvider.GetRequiredService<ITelegramUserRepository>();
        var user = await telegramUserRepo.GetByTelegramIdAsync(telegramUserId, cancellationToken);

        try
        {
            // Send DM with keyboard
            var sentMessage = await messageHandler.SendAsync(
                chatId: telegramUserId,
                text: messageText,
                replyMarkup: keyboard,
                ct: cancellationToken);

            _logger.LogDebug(
                "DM with keyboard sent successfully to {User} (MessageId: {MessageId})",
                user.ToLogDebug(telegramUserId),
                sentMessage.MessageId);

            // Update bot_dm_enabled flag to true
            await telegramUserRepo.SetBotDmEnabledAsync(telegramUserId, true, cancellationToken);

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
            _logger.LogWarning(
                "DM blocked for {User} (403 Forbidden) - cannot send keyboard message",
                user.ToLogDebug(telegramUserId));

            await telegramUserRepo.SetBotDmEnabledAsync(telegramUserId, false, cancellationToken);

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
            _logger.LogError(
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
