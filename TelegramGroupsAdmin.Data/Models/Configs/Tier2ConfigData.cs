namespace TelegramGroupsAdmin.Data.Models.Configs;

/// <summary>
/// Data layer representation of Tier2Config for EF Core JSON column mapping.
/// </summary>
public class Tier2ConfigData
{
    /// <summary>
    /// User-configurable priority order for cloud services
    /// </summary>
    public List<string> CloudQueuePriority { get; set; } = ["VirusTotal"];

    /// <summary>
    /// VirusTotal configuration
    /// </summary>
    public VirusTotalConfigData VirusTotal { get; set; } = new();

    /// <summary>
    /// Fail-open when all cloud services exhausted
    /// </summary>
    public bool FailOpenWhenExhausted { get; set; } = true;
}
