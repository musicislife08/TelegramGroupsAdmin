namespace TelegramGroupsAdmin.Configuration;

/// <summary>
/// Configuration for warning system.
/// Stored in configs table as JSONB.
/// Phase 4.11: Warning/Points System
/// </summary>
public class WarningSystemConfig
{
    /// <summary>
    /// Enable automatic ban after reaching threshold.
    /// </summary>
    public bool AutoBanEnabled { get; set; }

    /// <summary>
    /// Number of warnings before auto-ban (0 = disabled).
    /// </summary>
    public int AutoBanThreshold { get; set; }

    /// <summary>
    /// Reason shown when user is auto-banned.
    /// Supports {count} placeholder for warning count.
    /// </summary>
    public string AutoBanReason { get; set; } = string.Empty;

    /// <summary>
    /// Default configuration.
    /// </summary>
    public static WarningSystemConfig Default => new()
    {
        AutoBanEnabled = true,
        AutoBanThreshold = 3,
        AutoBanReason = "Automatic ban after {count} warnings"
    };
}
