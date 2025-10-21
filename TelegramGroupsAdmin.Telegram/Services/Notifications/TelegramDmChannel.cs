using Microsoft.Extensions.Logging;

namespace TelegramGroupsAdmin.Telegram.Services.Notifications;

/// <summary>
/// Telegram DM notification channel - delegates to DmDeliveryService for actual delivery
/// </summary>
public class TelegramDmChannel : INotificationChannel
{
    private readonly ILogger<TelegramDmChannel> _logger;
    private readonly IDmDeliveryService _dmDeliveryService;

    public string ChannelName => "telegram-dm";

    public TelegramDmChannel(
        ILogger<TelegramDmChannel> logger,
        IDmDeliveryService dmDeliveryService)
    {
        _logger = logger;
        _dmDeliveryService = dmDeliveryService;
    }

    public async Task<DeliveryResult> SendAsync(
        string recipient,
        Notification notification,
        CancellationToken cancellationToken = default)
    {
        // Parse recipient as Telegram user ID
        if (!long.TryParse(recipient, out var telegramUserId))
        {
            _logger.LogError("Invalid Telegram user ID: {Recipient}", recipient);
            return new DeliveryResult(false, "Invalid recipient format");
        }

        // Delegate to DmDeliveryService with queue-on-failure behavior
        var result = await _dmDeliveryService.SendDmWithQueueAsync(
            telegramUserId,
            notification.Type,
            notification.Message,
            cancellationToken);

        // Convert DmDeliveryResult to DeliveryResult
        return new DeliveryResult(
            Success: result.DmSent,
            ErrorMessage: result.ErrorMessage);
    }
}
