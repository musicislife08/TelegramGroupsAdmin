using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Identity;

namespace TelegramGroupsAdmin.Telegram.Services.Notifications;

/// <summary>
/// Telegram DM notification channel - delegates to DmDeliveryService for actual delivery.
/// Sends <see cref="Notification.Message"/> via <c>SendDmWithEntitiesAsync</c> using its text
/// and entities (no parse_mode). A plain message carries an empty entity list.
/// </summary>
public class TelegramDmChannel : INotificationChannel
{
    private readonly ILogger<TelegramDmChannel> _logger;
    private readonly IBotDmService _dmDeliveryService;
    private readonly IUserIdentityService _identityService;

    public string ChannelName => "telegram-dm";

    public TelegramDmChannel(
        ILogger<TelegramDmChannel> logger,
        IBotDmService dmDeliveryService,
        IUserIdentityService identityService)
    {
        _logger = logger;
        _dmDeliveryService = dmDeliveryService;
        _identityService = identityService;
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

        // Entity-based rendering only — ParseMode.Html is gone entirely. A plain message
        // carries an empty entity list, so a single send path covers both cases.
        var user = await _identityService.ResolveAsync(telegramUserId, cancellationToken);
        var result = await _dmDeliveryService.SendDmWithEntitiesAsync(
            user,
            notification.Type,
            notification.Message.Text,
            notification.Message.Entities,
            cancellationToken);

        return new DeliveryResult(
            Success: result.DmSent,
            ErrorMessage: result.ErrorMessage);
    }
}
