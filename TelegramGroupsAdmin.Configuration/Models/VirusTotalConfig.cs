namespace TelegramGroupsAdmin.Configuration.Models;

/// <summary>
/// VirusTotal configuration
/// </summary>
public class VirusTotalConfig
{
    /// <summary>
    /// Enable/disable VirusTotal scanning
    /// </summary>
    public bool Enabled { get; set; } = false;  // Requires API key

    /// <summary>
    /// Daily request limit
    /// </summary>
    public int DailyLimit { get; set; } = 500;

    /// <summary>
    /// Per-minute request limit
    /// </summary>
    public int PerMinuteLimit { get; set; } = 4;
}
