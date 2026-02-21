namespace TelegramGroupsAdmin.Core.Models.BackgroundJobSettings;

/// <summary>
/// Settings for Profile Rescan job.
/// Periodically re-scans user profiles to detect changes (bio, channel, stories).
/// </summary>
public record ProfileRescanSettings
{
    /// <summary>
    /// Maximum number of users to scan per batch (default: 100).
    /// </summary>
    public int BatchSize { get; init; } = 100;

    /// <summary>
    /// Only re-scan users whose profile was last scanned longer ago than this duration.
    /// Friendly duration format: "1h", "2d", "1w", "1M".
    /// Parsed via TimeSpanUtilities.TryParseDuration.
    /// </summary>
    public string RescanAfter { get; init; } = "1w";
}
