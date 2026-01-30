namespace TelegramGroupsAdmin.Core.Models.BackgroundJobSettings;

/// <summary>
/// Settings for Data Cleanup job - configurable retention periods.
/// Uses duration strings (e.g., "30d", "1M", "1y") parsed by TimeSpanUtilities.
/// </summary>
public record DataCleanupSettings
{
    /// <summary>
    /// Default message retention period string (2 years).
    /// </summary>
    public const string DefaultMessageRetentionString = "2y";

    /// <summary>
    /// Fallback retention for message history if parsing fails (2 years).
    /// </summary>
    public static readonly TimeSpan DefaultMessageRetention = TimeSpan.FromDays(730);

    /// <summary>
    /// Fallback retention for reports if parsing fails (30 days).
    /// </summary>
    public static readonly TimeSpan DefaultReportRetention = TimeSpan.FromDays(30);

    /// <summary>
    /// Fallback retention for callback contexts and notifications if parsing fails (7 days).
    /// </summary>
    public static readonly TimeSpan DefaultShortRetention = TimeSpan.FromDays(7);

    /// <summary>
    /// Retention period for message history (default: 2 years).
    /// Messages with detection results (training data) are kept permanently.
    /// </summary>
    public string MessageRetention { get; init; } = DefaultMessageRetentionString;

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
