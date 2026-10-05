namespace TelegramGroupsAdmin.Core.Models.BackgroundJobSettings;

/// <summary>
/// Settings for Profile Rescan job.
/// Retries incomplete profile scans: users never scanned, and users whose latest scan could only
/// read the name. Fully scanned users are not rescanned on a timer.
/// </summary>
public record ProfileRescanSettings
{
    /// <summary>
    /// Maximum number of users to scan per batch (default: 100).
    /// </summary>
    public int BatchSize { get; init; } = 100;

    /// <summary>
    /// Wait before an incomplete scan is retried: only users last scanned longer ago than this.
    /// Friendly duration format: "1h", "2d", "1w", "1M".
    /// Parsed via TimeSpanUtilities.TryParseDuration.
    /// </summary>
    public string RescanAfter { get; init; } = "1w";

    /// <summary>
    /// Stop retrying a user once this many name-only scans were recorded since their last full scan
    /// (default: 3). The latest name-only verdict then stands until the user renames or an admin rescans.
    /// </summary>
    public int NameOnlyRetryLimit { get; init; } = 3;
}
