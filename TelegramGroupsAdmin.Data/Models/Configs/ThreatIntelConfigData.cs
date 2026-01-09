namespace TelegramGroupsAdmin.Data.Models.Configs;

/// <summary>
/// Data layer representation of ThreatIntelConfig for EF Core JSON column mapping.
/// </summary>
public class ThreatIntelConfigData
{
    public bool UseGlobal { get; set; } = true;

    public bool Enabled { get; set; } = true;

    public bool UseVirusTotal { get; set; }

    /// <summary>
    /// Timeout in seconds. Stored as double to avoid Npgsql interval parsing issues with ToJson().
    /// </summary>
    public double TimeoutSeconds { get; set; } = 30;

    public bool AlwaysRun { get; set; }
}
