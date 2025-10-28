namespace TelegramGroupsAdmin.Configuration.Models;

/// <summary>
/// Tier 1: Local scanner configuration (ClamAV only)
/// Note: YARA was removed - ClamAV provides superior coverage with 10M+ signatures
/// Note: Windows AMSI was removed - ClamAV + VirusTotal provides 96-98% coverage (sufficient for use case)
/// </summary>
public class Tier1Config
{
    /// <summary>
    /// ClamAV scanner settings
    /// </summary>
    public ClamAVConfig ClamAV { get; set; } = new();
}
