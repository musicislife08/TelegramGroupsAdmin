namespace TelegramGroupsAdmin.Configuration.Models;

/// <summary>
/// Web Push notification configuration stored in configs.web_push_config JSONB column
/// VAPID private key is stored separately in configs.vapid_private_key_encrypted (encrypted)
///
/// Key insight: VAPID public key is NOT a secret - it's sent to browsers during push subscription
/// Only the private key needs encryption.
/// </summary>
public class WebPushConfig
{
    /// <summary>
    /// Whether Web Push browser notifications are enabled
    /// When disabled, in-app bell notifications still work, but browser push is skipped
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Contact email for VAPID authentication (required by Web Push specification)
    /// Identifies the service operator if push providers need to contact regarding abuse
    /// If not set, falls back to the primary Owner account's email
    /// Example: admin@example.com
    /// </summary>
    public string? ContactEmail { get; set; }

    /// <summary>
    /// VAPID public key (base64 URL-safe encoded, 65 bytes when decoded)
    /// Auto-generated on first startup - do not modify manually
    /// This key is sent to browsers and is NOT a secret
    /// </summary>
    public string? VapidPublicKey { get; set; }

    /// <summary>
    /// Returns true if VAPID is configured (public key exists)
    /// Private key is checked separately from encrypted column
    /// </summary>
    public bool HasVapidPublicKey() => !string.IsNullOrWhiteSpace(VapidPublicKey);
}
