namespace TelegramGroupsAdmin.Data.Models.Configs;

/// <summary>
/// Data layer representation of SpacingConfig for EF Core JSON column mapping.
/// </summary>
public class SpacingConfigData
{
    public bool UseGlobal { get; set; } = true;

    public bool Enabled { get; set; } = true;

    public int MinWordsCount { get; set; } = 5;

    public int ShortWordLength { get; set; } = 3;

    public double ShortWordRatioThreshold { get; set; } = 0.7;

    public double SpaceRatioThreshold { get; set; } = 0.3;

    public int ConfidenceThreshold { get; set; } = 70;

    public bool AlwaysRun { get; set; }
}
