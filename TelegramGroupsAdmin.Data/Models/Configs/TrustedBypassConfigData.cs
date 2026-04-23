namespace TelegramGroupsAdmin.Data.Models.Configs;

/// <summary>
/// Data-layer DTO for the trusted-bypass section of the welcome-config
/// JSONB blob. Nullable on <see cref="WelcomeConfigData"/> because rows
/// predating the feature will not have the object.
/// </summary>
public class TrustedBypassConfigData
{
    public bool Enabled { get; set; }
    public string AnnouncementMessageAdmin { get; set; } = string.Empty;
    public string AnnouncementMessageTrusted { get; set; } = string.Empty;
    public int AnnouncementTtlSeconds { get; set; }
}
