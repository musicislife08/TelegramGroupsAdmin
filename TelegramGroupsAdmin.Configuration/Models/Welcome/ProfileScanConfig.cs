namespace TelegramGroupsAdmin.Configuration.Models.Welcome;

/// <summary>
/// Configuration for User API profile scanning on join.
/// </summary>
public class ProfileScanConfig
{
    /// <summary>
    /// Whether profile scanning is enabled
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Score threshold for automatic ban (0.0-5.0)
    /// </summary>
    public decimal BanThreshold { get; set; } = 4.0m;

    /// <summary>
    /// Score threshold for admin notification/review (0.0-5.0)
    /// </summary>
    public decimal NotifyThreshold { get; set; } = 2.0m;

    /// <summary>
    /// Whether to scan user profiles when they join a chat
    /// </summary>
    public bool ScanOnJoin { get; set; } = true;

    /// <summary>
    /// Whether to re-scan when Bot API profile fields change (name/username)
    /// </summary>
    public bool ScanOnProfileChange { get; set; } = true;
}
