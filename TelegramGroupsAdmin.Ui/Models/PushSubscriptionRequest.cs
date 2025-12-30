namespace TelegramGroupsAdmin.Ui.Models;

/// <summary>
/// Request to register a WebPush subscription.
/// </summary>
public record PushSubscriptionRequest(
    string Endpoint,
    string P256dh,
    string Auth
);
