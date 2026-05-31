using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using TelegramGroupsAdmin.Telegram.Services.Bot;

namespace TelegramGroupsAdmin.Telegram.Services.Notifications;

/// <summary>
/// Telegram DM notification channel - delegates to DmDeliveryService for actual delivery.
/// When the notification carries a <see cref="Notification.Telegram"/> entity payload, sends
/// it via <c>SendDmWithEntitiesAsync</c> (no parse_mode). Otherwise sends the plain-text
/// <see cref="Notification.Message"/> with an empty entity list — also no parse_mode.
/// </summary>
public class TelegramDmChannel : INotificationChannel
{
    private readonly ILogger<TelegramDmChannel> _logger;
    private readonly IBotDmService _dmDeliveryService;

    public string ChannelName => "telegram-dm";

    public TelegramDmChannel(
        ILogger<TelegramDmChannel> logger,
        IBotDmService dmDeliveryService)
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

        // Prefer entity-based rendering when available; fall back to plain text (no parse_mode).
        // Using SendDmWithEntitiesAsync for both paths means ParseMode.Html is gone entirely.
        string text;
        IReadOnlyList<MessageEntity> entities;

        if (notification.Telegram is { } tm)
        {
            text = tm.Text;
            entities = tm.Entities;
        }
        else
        {
            text = notification.Message;
            entities = [];
        }

        var result = await _dmDeliveryService.SendDmWithEntitiesAsync(
            Core.Models.UserIdentity.FromId(telegramUserId),
            notification.Type,
            text,
            entities,
            cancellationToken);

        return new DeliveryResult(
            Success: result.DmSent,
            ErrorMessage: result.ErrorMessage);
    }
}
