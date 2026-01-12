namespace TelegramGroupsAdmin.Core.Models.BackgroundJobSettings;

/// <summary>
/// Settings for Data Cleanup job - configurable retention periods.
/// Uses duration strings (e.g., "30d", "1M", "1y") parsed by TimeSpanUtilities.
/// </summary>
public record DataCleanupSettings
{
    /// <summary>
    /// Retention period for message history (default: 30 days).
    /// Messages with detection results (training data) are kept permanently.
    /// </summary>
    public string MessageRetention { get; init; } = "30d";

    /// <summary>
    /// Retention period for resolved reports (default: 30 days).
    /// Pending reports are never deleted.
    /// </summary>
    public string ReportRetention { get; init; } = "30d";

    /// <summary>
    /// Retention period for DM callback button contexts (default: 7 days).
    /// </summary>
    public string CallbackContextRetention { get; init; } = "7d";

    /// <summary>
    /// Retention period for read web notifications (default: 7 days).
    /// </summary>
    public string WebNotificationRetention { get; init; } = "7d";
}
