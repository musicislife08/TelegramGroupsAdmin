namespace TelegramGroupsAdmin.ContentDetection.Services;

/// <summary>
/// Scan result classification
/// </summary>
public enum ScanResultType
{
    /// <summary>
    /// File is clean - no threats detected
    /// </summary>
    Clean = 0,

    /// <summary>
    /// File contains malware or known threat
    /// </summary>
    Infected = 1,

    /// <summary>
    /// File is suspicious but not definitively malicious
    /// </summary>
    Suspicious = 2,

    /// <summary>
    /// Scan encountered an error
    /// </summary>
    Error = 3,

    /// <summary>
    /// Scan was skipped (e.g., file too large for scanner)
    /// </summary>
    Skipped = 4
}
