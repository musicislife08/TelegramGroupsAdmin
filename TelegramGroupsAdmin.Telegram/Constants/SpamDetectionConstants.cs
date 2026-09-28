namespace TelegramGroupsAdmin.Telegram.Constants;

/// <summary>
/// Constants for spam detection that are NOT admin-configurable.
/// These are implementation details of the ML training system and job scheduling.
///
/// NOTE: Detection thresholds (auto-ban, review queue) are now database-driven
/// via ContentDetectionConfig. See: AutoBanThreshold, ReviewQueueThreshold.
/// </summary>
public static class SpamDetectionConstants
{
    // ============================================================
    // JOB SCHEDULING CONSTANTS
    // Timing parameters for background cleanup jobs.
    // Not admin-configurable - these are race condition mitigations.
    // ============================================================

    /// <summary>
    /// Delay before executing cross-chat message cleanup job (15 seconds)
    /// Allows spambots to post across all chats before cleanup executes
    /// </summary>
    public const int CleanupJobDelaySeconds = 15;

    /// <summary>
    /// Deduplication window for cleanup job scheduling (30 seconds)
    /// Prevents duplicate cleanup jobs for the same user within this window
    /// </summary>
    public static readonly TimeSpan CleanupJobDeduplicationWindow = TimeSpan.FromSeconds(30);

    // ============================================================
    // MANUAL SPAM CLASSIFICATION CONSTANTS
    // Used by TrainingHandler when moderators mark messages as spam.
    // ============================================================

    /// <summary>
    /// Detection source identifier for manual moderator actions
    /// </summary>
    public const string ManualDetectionSource = "manual";

    /// <summary>
    /// Detection method identifier for manual moderator actions
    /// </summary>
    public const string ManualDetectionMethod = "Manual";

    /// <summary>
    /// Reason text for manual spam classification by moderator
    /// </summary>
    public const string ManualSpamReason = "Marked as spam by moderator";

    /// <summary>
    /// Reason text for auto-detected spam classification by the detection pipeline
    /// </summary>
    public const string AutoDetectedSpamReason = "Auto-detected spam";
}
