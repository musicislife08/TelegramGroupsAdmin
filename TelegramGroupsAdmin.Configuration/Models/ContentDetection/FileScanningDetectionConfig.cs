namespace TelegramGroupsAdmin.Configuration.Models.ContentDetection;

/// <summary>
/// File scanning detection configuration (detection flags only).
/// Infrastructure settings (ClamAV, VirusTotal connection) remain in FileScanningConfig.
/// </summary>
public class FileScanningDetectionConfig
{
    /// <summary>
    /// Whether to use global configuration instead of chat-specific overrides.
    /// Always true for global config (chat_id=0), can be true/false for chat configs.
    /// </summary>
    public bool UseGlobal { get; set; } = true;

    /// <summary>
    /// Whether file scanning is enabled for spam/malware detection
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Always run this check for all users (bypasses trust/admin status).
    /// Default is true since file scanning is a critical security check.
    /// </summary>
    public bool AlwaysRun { get; set; } = true;
}
