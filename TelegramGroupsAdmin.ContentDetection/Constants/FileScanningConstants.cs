namespace TelegramGroupsAdmin.ContentDetection.Constants;

/// <summary>
/// Constants for file scanning operations.
/// </summary>
public static class FileScanningConstants
{
    /// <summary>
    /// ClamAV hard limit - maximum file size that ClamAV can scan (2 GiB - 1 byte)
    /// </summary>
    public const long MaxClamAVSizeBytes = 2147483647L;

    /// <summary>
    /// Maximum number of retry attempts for ClamAV scans when transient errors occur
    /// </summary>
    public const int ClamAVMaxRetries = 3;

    /// <summary>
    /// Initial retry delay in milliseconds for ClamAV connection failures
    /// </summary>
    public const int ClamAVInitialRetryDelayMs = 500;

    /// <summary>
    /// VirusTotal minimum engine threshold - consider malicious if >= 2 engines detect it
    /// </summary>
    public const int VirusTotalMinEngineThreshold = 2;

    /// <summary>
    /// VirusTotal maximum number of poll attempts when waiting for analysis results
    /// </summary>
    public const int VirusTotalMaxPolls = 5;

    /// <summary>
    /// VirusTotal poll delay in minutes - wait time between polling for analysis results
    /// </summary>
    public const int VirusTotalPollDelayMinutes = 2;

    /// <summary>
    /// File scan cache TTL in hours - how long to cache scan results by hash
    /// </summary>
    public const int ScanCacheTtlHours = 24;
}
