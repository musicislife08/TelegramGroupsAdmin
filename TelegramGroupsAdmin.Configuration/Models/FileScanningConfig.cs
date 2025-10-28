namespace TelegramGroupsAdmin.Configuration.Models;

/// <summary>
/// Configuration model for file scanning system (Phase 4.17)
/// Supports two-tier architecture: local scanners (Tier 1) + cloud services (Tier 2)
/// Stored in configs table with ConfigType.FileScanning
/// </summary>
public class FileScanningConfig
{
    /// <summary>
    /// Tier 1 scanner configuration (local, unlimited)
    /// </summary>
    public Tier1Config Tier1 { get; set; } = new();

    /// <summary>
    /// Tier 2 cloud scanner configuration (quota-limited)
    /// </summary>
    public Tier2Config Tier2 { get; set; } = new();

    /// <summary>
    /// General file scanning settings
    /// </summary>
    public GeneralConfig General { get; set; } = new();

    /// <summary>
    /// Timestamp of last configuration change
    /// </summary>
    public DateTimeOffset LastModified { get; set; } = DateTimeOffset.UtcNow;
}
