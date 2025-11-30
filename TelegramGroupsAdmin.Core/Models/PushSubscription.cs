namespace TelegramGroupsAdmin.Core.Models;

/// <summary>
/// Domain model for browser push notification subscriptions
/// Used by Web Push API to send browser notifications
/// </summary>
public class PushSubscription
{
    public long Id { get; set; }
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Push subscription endpoint URL (provided by browser)
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>
    /// P-256 Diffie-Hellman public key for encryption
    /// </summary>
    public string P256dh { get; set; } = string.Empty;

    /// <summary>
    /// Authentication secret for push message encryption
    /// </summary>
    public string Auth { get; set; } = string.Empty;

    /// <summary>
    /// User agent string of the browser that created the subscription
    /// </summary>
    public string? UserAgent { get; set; }

    /// <summary>
    /// When the subscription was created
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
