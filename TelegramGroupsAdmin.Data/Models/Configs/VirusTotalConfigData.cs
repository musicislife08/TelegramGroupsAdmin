namespace TelegramGroupsAdmin.Data.Models.Configs;

/// <summary>
/// Data layer representation of VirusTotalConfig for EF Core JSON column mapping.
/// </summary>
public class VirusTotalConfigData
{
    /// <summary>
    /// Enable/disable VirusTotal scanning
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Daily request limit
    /// </summary>
    public int DailyLimit { get; set; } = 500;

    /// <summary>
    /// Per-minute request limit
    /// </summary>
    public int PerMinuteLimit { get; set; } = 4;
}
