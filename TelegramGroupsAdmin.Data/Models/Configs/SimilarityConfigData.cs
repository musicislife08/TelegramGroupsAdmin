namespace TelegramGroupsAdmin.Data.Models.Configs;

/// <summary>
/// Data layer representation of SimilarityConfig for EF Core JSON column mapping.
/// </summary>
public class SimilarityConfigData
{
    public bool UseGlobal { get; set; } = true;

    public bool Enabled { get; set; } = true;

    public double Threshold { get; set; } = 0.5;

    public bool AlwaysRun { get; set; }
}
