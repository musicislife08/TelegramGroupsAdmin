namespace TelegramGroupsAdmin.Data.Models.Configs;

/// <summary>
/// Data layer representation of FileScanningConfig for EF Core JSON column mapping.
/// Maps to business model via ToModel/ToDto extensions.
/// </summary>
public class FileScanningConfigData
{
    /// <summary>
    /// Tier 1 scanner configuration (local)
    /// </summary>
    public Tier1ConfigData Tier1 { get; set; } = new();

    /// <summary>
    /// Tier 2 cloud scanner configuration
    /// </summary>
    public Tier2ConfigData Tier2 { get; set; } = new();

    /// <summary>
    /// General file scanning settings
    /// </summary>
    public GeneralConfigData General { get; set; } = new();

    /// <summary>
    /// Timestamp of last configuration change
    /// </summary>
    public DateTimeOffset LastModified { get; set; } = DateTimeOffset.UtcNow;
}
