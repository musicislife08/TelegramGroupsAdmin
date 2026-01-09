namespace TelegramGroupsAdmin.Data.Models.Configs;

/// <summary>
/// Data layer representation of WebPushConfig for EF Core JSON column mapping.
/// Maps to business model via ToModel/ToDto extensions.
/// </summary>
public class WebPushConfigData
{
    /// <summary>
    /// Whether Web Push browser notifications are enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Contact email for VAPID authentication
    /// </summary>
    public string? ContactEmail { get; set; }

    /// <summary>
    /// VAPID public key (base64 URL-safe encoded)
    /// </summary>
    public string? VapidPublicKey { get; set; }
}
