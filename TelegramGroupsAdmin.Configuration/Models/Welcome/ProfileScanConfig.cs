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

    public const decimal DefaultBanThreshold = 4.0m;
    public const decimal DefaultNotifyThreshold = 2.0m;
    public const string DefaultExplicitUsernameRedactionText = "[explicit username redacted]";

    /// <summary>
    /// Score threshold for automatic ban (0.0-5.0)
    /// </summary>
    public decimal BanThreshold { get; set; } = DefaultBanThreshold;

    /// <summary>
    /// Score threshold for admin notification/review (0.0-5.0)
    /// </summary>
    public decimal NotifyThreshold { get; set; } = DefaultNotifyThreshold;

    /// <summary>
    /// Whether to scan user profiles when they join a chat
    /// </summary>
    public bool ScanOnJoin { get; set; } = true;

    /// <summary>
    /// Whether to re-scan when Bot API profile fields change (name/username)
    /// </summary>
    public bool ScanOnProfileChange { get; set; } = true;

    /// <summary>
    /// When true, replace the banned user's display name in public chat posts
    /// (e.g., ban-celebration captions) with <see cref="ExplicitUsernameRedactionText"/>
    /// if the most recent profile scan flagged the display text as explicit.
    /// </summary>
    public bool MaskExplicitUsername { get; set; } = true;

    /// <summary>
    /// Text substituted for the banned user's display name in public chat posts
    /// when <see cref="MaskExplicitUsername"/> is true and the AI flagged the
    /// display text as explicit.
    /// </summary>
    public string ExplicitUsernameRedactionText { get; set; } = DefaultExplicitUsernameRedactionText;
}
