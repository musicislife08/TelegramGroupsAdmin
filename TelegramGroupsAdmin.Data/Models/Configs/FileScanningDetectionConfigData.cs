namespace TelegramGroupsAdmin.Data.Models.Configs;

/// <summary>
/// Data layer representation of FileScanningDetectionConfig for EF Core JSON column mapping.
/// </summary>
public class FileScanningDetectionConfigData
{
    public bool UseGlobal { get; set; } = true;

    public bool Enabled { get; set; } = true;

    public bool AlwaysRun { get; set; } = true;
}
