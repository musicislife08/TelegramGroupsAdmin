namespace TelegramGroupsAdmin.Data.Models.Configs;

/// <summary>
/// Data layer representation of SeoScrapingConfig for EF Core JSON column mapping.
/// </summary>
public class SeoScrapingConfigData
{
    public bool UseGlobal { get; set; } = true;

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Timeout in seconds. Stored as double to avoid Npgsql interval parsing issues with ToJson().
    /// </summary>
    public double TimeoutSeconds { get; set; } = 10;

    public bool AlwaysRun { get; set; }
}
