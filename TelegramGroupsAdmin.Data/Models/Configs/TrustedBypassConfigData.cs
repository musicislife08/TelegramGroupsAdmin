namespace TelegramGroupsAdmin.Data.Models.Configs;

/// <summary>
/// Data-layer DTO for trusted bypass configuration. Serialized as part of the
/// welcome-config JSONB blob. Nullable on <see cref="WelcomeConfigData"/> because
/// existing JSONB blobs may not have this key yet.
/// </summary>
public class TrustedBypassConfigData
{
    /// <summary>
    /// Whether trusted users bypass the welcome CAPTCHA and receive an announcement instead.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Announcement message posted in place of the CAPTCHA when a trusted user joins.
    /// </summary>
    public string AnnouncementMessage { get; set; } = string.Empty;

    /// <summary>
    /// Time-to-live in seconds before the announcement message is auto-deleted.
    /// </summary>
    public int AnnouncementTtlSeconds { get; set; }
}
