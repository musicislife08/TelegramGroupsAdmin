namespace TelegramGroupsAdmin.Data.Models.Configs;

/// <summary>
/// Data layer representation of UrlBlocklistConfig for EF Core JSON column mapping.
/// </summary>
public class UrlBlocklistConfigData
{
    public bool UseGlobal { get; set; } = true;

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Cache duration in seconds. Stored as double to avoid Npgsql interval parsing issues with ToJson().
    /// </summary>
    public double CacheDurationSeconds { get; set; } = 86_400; // 24 hours

    public bool AlwaysRun { get; set; }
}
