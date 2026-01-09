namespace TelegramGroupsAdmin.Data.Models.Configs;

/// <summary>
/// Data layer representation of StopWordsConfig for EF Core JSON column mapping.
/// </summary>
public class StopWordsConfigData
{
    public bool UseGlobal { get; set; } = true;

    public bool Enabled { get; set; } = true;

    public int ConfidenceThreshold { get; set; } = 50;

    public bool AlwaysRun { get; set; }
}
