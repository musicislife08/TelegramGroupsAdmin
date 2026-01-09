namespace TelegramGroupsAdmin.Data.Models.Configs;

/// <summary>
/// Data layer representation of CasConfig for EF Core JSON column mapping.
/// </summary>
public class CasConfigData
{
    public bool UseGlobal { get; set; } = true;

    public bool Enabled { get; set; } = true;

    public string ApiUrl { get; set; } = "https://api.cas.chat";

    /// <summary>
    /// Timeout in seconds. Stored as double to avoid Npgsql interval parsing issues with ToJson().
    /// </summary>
    public double TimeoutSeconds { get; set; } = 5;

    public string? UserAgent { get; set; }

    public bool AlwaysRun { get; set; }
}
