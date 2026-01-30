namespace TelegramGroupsAdmin.Configuration.Models.Welcome;

/// <summary>
/// Configuration for impersonation detection.
/// </summary>
public class ImpersonationConfig
{
    /// <summary>
    /// Enable/disable impersonation detection on user join.
    /// </summary>
    public bool Enabled { get; set; } = true;

    // Future: thresholds for name/photo similarity could go here
}
