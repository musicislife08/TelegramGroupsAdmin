using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Core.JobPayloads;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Centralized DM delivery service with consistent bot_dm_enabled tracking and fallback handling.
/// Singleton service that creates scopes internally for repository access.
/// </summary>
public class DmDeliveryService : IDmDeliveryService
{
    private readonly ILogger<DmDeliveryService> _logger;
    private readonly ITelegramBotClientFactory _botClientFactory;
    private readonly IServiceProvider _serviceProvider;
    private readonly IJobScheduler _jobScheduler;

    public DmDeliveryService(
        ILogger<DmDeliveryService> logger,
        ITelegramBotClientFactory botClientFactory,
        IServiceProvider serviceProvider,
        IJobScheduler jobScheduler)
    {
        _logger = logger;
        _botClientFactory = botClientFactory;
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
        var operations = await _botClientFactory.GetOperationsAsync();

        try
        {
            // Attempt to send DM
            await operations.SendMessageAsync(
                chatId: telegramUserId,
                text: messageText,
                ct: cancellationToken);

            _logger.LogInformation(
                "DM sent successfully to user {UserId}",
                telegramUserId);

            // Update bot_dm_enabled flag to true (user can receive DMs)
            using (var scope = _serviceProvider.CreateScope())
            {
                var telegramUserRepository = scope.ServiceProvider.GetRequiredService<ITelegramUserRepository>();
                await telegramUserRepository.SetBotDmEnabledAsync(telegramUserId, true, cancellationToken);
            }

            return new DmDeliveryResult
            {
                DmSent = true,
                FallbackUsed = false,
                Failed = false
            };
        }
        catch (ApiRequestException ex) when (ex.ErrorCode == 403)
        {
            // User has blocked the bot or hasn't started a DM
            _logger.LogWarning(
                "DM blocked for user {UserId} (403 Forbidden){FallbackInfo}",
                telegramUserId,
                fallbackChatId.HasValue ? $" - falling back to chat {fallbackChatId.Value}" : " - no fallback configured");

            // Update bot_dm_enabled flag to false
            using (var scope = _serviceProvider.CreateScope())
            {
                var telegramUserRepository = scope.ServiceProvider.GetRequiredService<ITelegramUserRepository>();
                await telegramUserRepository.SetBotDmEnabledAsync(telegramUserId, false, cancellationToken);
            }

            // If fallback chat is configured, post message there
            if (fallbackChatId.HasValue)
            {
                return await SendFallbackToChatAsync(
                    operations,
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
                "Failed to send DM to user {UserId}",
                telegramUserId);

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
        var operations = await _botClientFactory.GetOperationsAsync();

        try
        {
            // Attempt to send DM
            await operations.SendMessageAsync(
                chatId: telegramUserId,
                text: messageText,
                ct: cancellationToken);

            _logger.LogInformation(
                "DM sent successfully to user {UserId} (notification type: {NotificationType})",
                telegramUserId,
                notificationType);

            // Update bot_dm_enabled flag to true (user can receive DMs)
            using (var scope = _serviceProvider.CreateScope())
            {
                var telegramUserRepository = scope.ServiceProvider.GetRequiredService<ITelegramUserRepository>();
                await telegramUserRepository.SetBotDmEnabledAsync(telegramUserId, true, cancellationToken);
            }

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
                "DM blocked for user {UserId} - queueing {NotificationType} notification for later delivery",
                telegramUserId,
                notificationType);

            // Update bot_dm_enabled flag to false and queue notification
            using (var scope = _serviceProvider.CreateScope())
            {
                var telegramUserRepository = scope.ServiceProvider.GetRequiredService<ITelegramUserRepository>();
                var pendingNotificationsRepository = scope.ServiceProvider.GetRequiredService<IPendingNotificationsRepository>();

                await telegramUserRepository.SetBotDmEnabledAsync(telegramUserId, false, cancellationToken);

                // Queue notification for later delivery
                await pendingNotificationsRepository.AddPendingNotificationAsync(
                    telegramUserId,
                    notificationType,
                    messageText,
                    cancellationToken: cancellationToken);
            }

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
                    "Failed to send DM to user {UserId} - network unavailable (notification type: {NotificationType})",
                    telegramUserId,
                    notificationType);
            }
            else
            {
                _logger.LogError(
                    ex,
                    "Failed to send DM to user {UserId} (notification type: {NotificationType})",
                    telegramUserId,
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
        ITelegramOperations operations,
        long chatId,
        string messageText,
        int? autoDeleteSeconds,
        CancellationToken cancellationToken)
    {
        try
        {
            var fallbackMessage = await operations.SendMessageAsync(
                chatId: chatId,
                text: messageText,
                ct: cancellationToken);

            _logger.LogInformation(
                "Sent fallback message {MessageId} in chat {ChatId}{DeleteInfo}",
                fallbackMessage.MessageId,
                chatId,
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
                    "Failed to send fallback message in chat {ChatId} - network unavailable",
                    chatId);
            }
            else
            {
                _logger.LogError(
                    ex,
                    "Failed to send fallback message in chat {ChatId}",
                    chatId);
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
        var operations = await _botClientFactory.GetOperationsAsync();

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
                    await operations.SendPhotoAsync(
                        chatId: telegramUserId,
                        photo: InputFile.FromStream(photoStream, Path.GetFileName(photoPath)),
                        caption: messageText,
                        parseMode: ParseMode.MarkdownV2,
                        ct: cancellationToken);

                    _logger.LogInformation("DM with photo sent successfully to user {UserId}", telegramUserId);
                }
                else if (!string.IsNullOrWhiteSpace(videoPath) && File.Exists(videoPath))
                {
                    // Send video with caption
                    await using var videoStream = File.OpenRead(videoPath);
                    await operations.SendVideoAsync(
                        chatId: telegramUserId,
                        video: InputFile.FromStream(videoStream, Path.GetFileName(videoPath)),
                        caption: messageText,
                        parseMode: ParseMode.MarkdownV2,
                        ct: cancellationToken);

                    _logger.LogInformation("DM with video sent successfully to user {UserId}", telegramUserId);
                }
                else
                {
                    // Media path provided but file doesn't exist - fallback to text only
                    _logger.LogWarning("Media file not found (photo: {PhotoPath}, video: {VideoPath}), sending text-only DM to user {UserId}",
                        photoPath, videoPath, telegramUserId);

                    await operations.SendMessageAsync(
                        chatId: telegramUserId,
                        text: messageText,
                        parseMode: ParseMode.MarkdownV2,
                        ct: cancellationToken);
                }
            }
            else
            {
                // No media - send text only
                await operations.SendMessageAsync(
                    chatId: telegramUserId,
                    text: messageText,
                    parseMode: ParseMode.MarkdownV2,
                    ct: cancellationToken);

                _logger.LogInformation("DM sent successfully to user {UserId}", telegramUserId);
            }

            // Update bot_dm_enabled flag to true
            using (var updateScope = _serviceProvider.CreateScope())
            {
                var telegramUserRepo = updateScope.ServiceProvider.GetRequiredService<ITelegramUserRepository>();
                await telegramUserRepo.SetBotDmEnabledAsync(telegramUserId, true, cancellationToken);
            }

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
            _logger.LogInformation("User {UserId} has blocked bot DMs (403), queuing notification", telegramUserId);

            using (var updateScope = _serviceProvider.CreateScope())
            {
                var telegramUserRepo = updateScope.ServiceProvider.GetRequiredService<ITelegramUserRepository>();
                await telegramUserRepo.SetBotDmEnabledAsync(telegramUserId, false, cancellationToken);

                // Queue notification for later delivery
                var notificationRepo = updateScope.ServiceProvider.GetRequiredService<IPendingNotificationsRepository>();
                await notificationRepo.AddPendingNotificationAsync(telegramUserId, notificationType, messageText, cancellationToken: cancellationToken);
            }

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
            _logger.LogError(ex, "Failed to send DM with media to user {UserId}", telegramUserId);
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
