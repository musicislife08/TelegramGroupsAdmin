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

/// <summary>
/// Tier 1: Local scanner configuration (ClamAV only)
/// Note: YARA was removed - ClamAV provides superior coverage with 10M+ signatures
/// Note: Windows AMSI was removed - ClamAV + VirusTotal provides 96-98% coverage (sufficient for use case)
/// </summary>
public class Tier1Config
{
    /// <summary>
    /// ClamAV scanner settings
    /// </summary>
    public ClamAVConfig ClamAV { get; set; } = new();
}

/// <summary>
/// ClamAV configuration
/// </summary>
public class ClamAVConfig
{
    /// <summary>
    /// Enable/disable ClamAV scanning
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// ClamAV daemon host
    /// </summary>
    public string Host { get; set; } = "localhost";

    /// <summary>
    /// ClamAV daemon port
    /// </summary>
    public int Port { get; set; } = 3310;

    /// <summary>
    /// Scan timeout in seconds
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>
/// Tier 2: Cloud scanner configuration
/// </summary>
public class Tier2Config
{
    /// <summary>
    /// User-configurable priority order for cloud services
    /// Services are tried in this order until one succeeds
    /// </summary>
    public List<string> CloudQueuePriority { get; set; } = new() { "VirusTotal", "MetaDefender", "HybridAnalysis", "Intezer" };

    /// <summary>
    /// VirusTotal configuration
    /// </summary>
    public VirusTotalConfig VirusTotal { get; set; } = new();

    /// <summary>
    /// MetaDefender configuration
    /// </summary>
    public MetaDefenderConfig MetaDefender { get; set; } = new();

    /// <summary>
    /// Hybrid Analysis configuration
    /// </summary>
    public HybridAnalysisConfig HybridAnalysis { get; set; } = new();

    /// <summary>
    /// Intezer configuration
    /// </summary>
    public IntezerConfig Intezer { get; set; } = new();

    /// <summary>
    /// Fail-open when all cloud services exhausted
    /// true = allow file through, false = block file
    /// </summary>
    public bool FailOpenWhenExhausted { get; set; } = true;
}

/// <summary>
/// VirusTotal configuration
/// </summary>
public class VirusTotalConfig
{
    /// <summary>
    /// Enable/disable VirusTotal scanning
    /// </summary>
    public bool Enabled { get; set; } = false;  // Requires API key

    /// <summary>
    /// Daily request limit
    /// </summary>
    public int DailyLimit { get; set; } = 500;

    /// <summary>
    /// Per-minute request limit
    /// </summary>
    public int PerMinuteLimit { get; set; } = 4;
}

/// <summary>
/// MetaDefender configuration
/// </summary>
public class MetaDefenderConfig
{
    /// <summary>
    /// Enable/disable MetaDefender scanning
    /// </summary>
    public bool Enabled { get; set; } = false;  // Requires API key

    /// <summary>
    /// Daily request limit
    /// </summary>
    public int DailyLimit { get; set; } = 40;
}

/// <summary>
/// Hybrid Analysis configuration
/// </summary>
public class HybridAnalysisConfig
{
    /// <summary>
    /// Enable/disable Hybrid Analysis scanning
    /// </summary>
    public bool Enabled { get; set; } = false;  // Requires API key

    /// <summary>
    /// Monthly request limit
    /// </summary>
    public int MonthlyLimit { get; set; } = 30;
}

/// <summary>
/// Intezer configuration
/// </summary>
public class IntezerConfig
{
    /// <summary>
    /// Enable/disable Intezer scanning
    /// </summary>
    public bool Enabled { get; set; } = false;  // Requires API key

    /// <summary>
    /// Monthly request limit
    /// </summary>
    public int MonthlyLimit { get; set; } = 10;
}

/// <summary>
/// General file scanning settings
/// </summary>
public class GeneralConfig
{
    /// <summary>
    /// Enable result caching (24hr TTL)
    /// </summary>
    public bool CacheEnabled { get; set; } = true;

    /// <summary>
    /// Cache TTL in hours
    /// </summary>
    public int CacheTtlHours { get; set; } = 24;

    /// <summary>
    /// File types to scan (by extension)
    /// Empty list = scan all files
    /// </summary>
    public List<string> ScanFileTypes { get; set; } = new()
    {
        ".exe", ".dll", ".zip", ".rar", ".7z", ".pdf",
        ".doc", ".docx", ".xls", ".xlsx", ".apk", ".dmg",
        ".pkg", ".bat", ".ps1", ".sh"
    };

    /// <summary>
    /// Maximum file size to scan in bytes
    /// Default: 4.5GB (Telegram Premium supports up to 4GB files)
    /// Set lower for performance if large file scans are too slow
    /// </summary>
    public long MaxFileSizeBytes { get; set; } = 4831838208;  // 4.5GB in bytes

    /// <summary>
    /// Always run for all users (bypass trust/admin status)
    /// Integration with Phase 4.14 Critical Checks
    /// </summary>
    public bool AlwaysRunForAllUsers { get; set; } = true;
}
