using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using TelegramGroupsAdmin.Telegram.Abstractions.Jobs;
using TelegramGroupsAdmin.Telegram.Abstractions.Services;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Centralized DM delivery service with consistent bot_dm_enabled tracking and fallback handling.
/// Singleton service that creates scopes internally for repository access.
/// </summary>
public class DmDeliveryService : IDmDeliveryService
{
    private readonly ILogger<DmDeliveryService> _logger;
    private readonly TelegramBotClientFactory _botClientFactory;
    private readonly IServiceProvider _serviceProvider;
    private readonly TelegramConfigLoader _configLoader;

    public DmDeliveryService(
        ILogger<DmDeliveryService> logger,
        TelegramBotClientFactory botClientFactory,
        IServiceProvider serviceProvider,
        TelegramConfigLoader configLoader)
    {
        _logger = logger;
        _botClientFactory = botClientFactory;
        _serviceProvider = serviceProvider;
        _configLoader = configLoader;
    }

    public async Task<DmDeliveryResult> SendDmAsync(
        long telegramUserId,
        string messageText,
        long? fallbackChatId = null,
        int? autoDeleteSeconds = null,
        CancellationToken cancellationToken = default)
    {
        var (botToken, _, apiServerUrl) = await _configLoader.LoadConfigAsync();
        var botClient = _botClientFactory.GetOrCreate(botToken, apiServerUrl);

        try
        {
            // Attempt to send DM
            await botClient.SendMessage(
                chatId: telegramUserId,
                text: messageText,
                cancellationToken: cancellationToken);

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
                    botClient,
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
        var (botToken, _, apiServerUrl) = await _configLoader.LoadConfigAsync();
        var botClient = _botClientFactory.GetOrCreate(botToken, apiServerUrl);

        try
        {
            // Attempt to send DM
            await botClient.SendMessage(
                chatId: telegramUserId,
                text: messageText,
                cancellationToken: cancellationToken);

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
        ITelegramBotClient botClient,
        long chatId,
        string messageText,
        int? autoDeleteSeconds,
        CancellationToken cancellationToken)
    {
        try
        {
            var fallbackMessage = await botClient.SendMessage(
                chatId: chatId,
                text: messageText,
                cancellationToken: cancellationToken);

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

                await TickerQUtilities.ScheduleJobAsync(
                    _serviceProvider,
                    _logger,
                    "DeleteMessage",
                    deletePayload,
                    delaySeconds: autoDeleteSeconds.Value,
                    retries: 0);
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
        var (botToken, _, apiServerUrl) = await _configLoader.LoadConfigAsync();
        var botClient = _botClientFactory.GetOrCreate(botToken, apiServerUrl);

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
                    await botClient.SendPhoto(
                        chatId: telegramUserId,
                        photo: global::Telegram.Bot.Types.InputFile.FromStream(photoStream, Path.GetFileName(photoPath)),
                        caption: messageText,
                        parseMode: global::Telegram.Bot.Types.Enums.ParseMode.MarkdownV2,
                        cancellationToken: cancellationToken);

                    _logger.LogInformation("DM with photo sent successfully to user {UserId}", telegramUserId);
                }
                else if (!string.IsNullOrWhiteSpace(videoPath) && File.Exists(videoPath))
                {
                    // Send video with caption
                    await using var videoStream = File.OpenRead(videoPath);
                    await botClient.SendVideo(
                        chatId: telegramUserId,
                        video: global::Telegram.Bot.Types.InputFile.FromStream(videoStream, Path.GetFileName(videoPath)),
                        caption: messageText,
                        parseMode: global::Telegram.Bot.Types.Enums.ParseMode.MarkdownV2,
                        cancellationToken: cancellationToken);

                    _logger.LogInformation("DM with video sent successfully to user {UserId}", telegramUserId);
                }
                else
                {
                    // Media path provided but file doesn't exist - fallback to text only
                    _logger.LogWarning("Media file not found (photo: {PhotoPath}, video: {VideoPath}), sending text-only DM to user {UserId}",
                        photoPath, videoPath, telegramUserId);

                    await botClient.SendMessage(
                        chatId: telegramUserId,
                        text: messageText,
                        parseMode: global::Telegram.Bot.Types.Enums.ParseMode.MarkdownV2,
                        linkPreviewOptions: new global::Telegram.Bot.Types.LinkPreviewOptions { IsDisabled = true },
                        cancellationToken: cancellationToken);
                }
            }
            else
            {
                // No media - send text only
                await botClient.SendMessage(
                    chatId: telegramUserId,
                    text: messageText,
                    parseMode: global::Telegram.Bot.Types.Enums.ParseMode.MarkdownV2,
                    linkPreviewOptions: new global::Telegram.Bot.Types.LinkPreviewOptions { IsDisabled = true },
                    cancellationToken: cancellationToken);

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
