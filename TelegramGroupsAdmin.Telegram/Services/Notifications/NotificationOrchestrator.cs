using Microsoft.Extensions.Logging;

namespace TelegramGroupsAdmin.Telegram.Services.Notifications;

/// <summary>
/// Orchestrates notification delivery across multiple channels
/// </summary>
public class NotificationOrchestrator : INotificationOrchestrator
{
    private readonly ILogger<NotificationOrchestrator> _logger;
    private readonly IEnumerable<INotificationChannel> _channels;
    private readonly Dictionary<string, INotificationChannel> _channelMap;

    public NotificationOrchestrator(
        ILogger<NotificationOrchestrator> logger,
        IEnumerable<INotificationChannel> channels)
    {
        _logger = logger;
        _channels = channels;
        _channelMap = channels.ToDictionary(c => c.ChannelName, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<DeliveryResult> SendAsync(
        string channelName,
        string recipient,
        Notification notification,
        CancellationToken cancellationToken = default)
    {
        if (!_channelMap.TryGetValue(channelName, out var channel))
        {
            _logger.LogError("Unknown notification channel: {ChannelName}", channelName);
            return new DeliveryResult(false, $"Unknown channel: {channelName}");
        }

        _logger.LogDebug(
            "Sending {NotificationType} notification to {Recipient} via {ChannelName}",
            notification.Type,
            recipient,
            channelName);

        return await channel.SendAsync(recipient, notification, cancellationToken);
    }

    public Task<DeliveryResult> SendTelegramDmAsync(
        long telegramUserId,
        Notification notification,
        CancellationToken cancellationToken = default)
    {
        return SendAsync("telegram-dm", telegramUserId.ToString(), notification, cancellationToken);
    }
}
