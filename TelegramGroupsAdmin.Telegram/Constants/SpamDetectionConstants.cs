namespace TelegramGroupsAdmin.Telegram.Constants;

/// <summary>
/// Constants for spam detection that are NOT admin-configurable.
/// These are implementation details of the ML training system and job scheduling.
///
/// NOTE: Detection thresholds (auto-ban, review queue, veto) are now database-driven
/// via ContentDetectionConfig. See: AutoBanThreshold, ReviewQueueThreshold, MaxConfidenceVetoThreshold.
/// </summary>
public static class SpamDetectionConstants
{
    // ============================================================
    // TRAINING DATA QUALITY THRESHOLDS
    // These ensure only high-quality samples enter the training dataset.
    // Not admin-configurable - changing these affects ML model quality.
    // ============================================================

    /// <summary>
    /// Minimum OpenAI confidence required for training data (85%)
    /// Used in DetermineIfTrainingWorthy to filter high-quality samples
    /// </summary>
    public const int OpenAIConfidentThreshold = 85;

    /// <summary>
    /// Minimum net confidence required for training data (80%)
    /// Prevents low-quality auto-detections from polluting training dataset
    /// </summary>
    public const int TrainingConfidenceThreshold = 80;

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
}
