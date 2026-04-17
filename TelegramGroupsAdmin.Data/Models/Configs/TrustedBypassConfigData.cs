namespace TelegramGroupsAdmin.Data.Models.Configs;

/// <summary>
/// Data-layer DTO for trusted bypass configuration. Serialized as part of the
/// welcome-config JSONB blob. Nullable on <see cref="WelcomeConfigData"/> because
/// existing JSONB blobs may not have this key yet.
/// </summary>
public class TrustedBypassConfigData
{
    public bool Enabled { get; set; }
    public string AnnouncementMessage { get; set; } = string.Empty;
    public int AnnouncementTtlSeconds { get; set; }
}
