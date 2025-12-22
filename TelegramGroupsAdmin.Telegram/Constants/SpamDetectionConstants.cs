namespace TelegramGroupsAdmin.Telegram.Constants;

/// <summary>
/// Centralized constants for spam detection confidence thresholds and decision logic.
/// </summary>
public static class SpamDetectionConstants
{
    /// <summary>
    /// Minimum net confidence required to trigger auto-ban (50%)
    /// </summary>
    public const int AutoBanNetConfidenceThreshold = 50;

    /// <summary>
    /// Minimum net confidence to create borderline report for admin review (0%)
    /// </summary>
    public const int BorderlineNetConfidenceThreshold = 0;

    /// <summary>
    /// Minimum OpenAI confidence required to be considered "confident" (85%)
    /// Used for auto-ban decisions and training data quality filtering
    /// </summary>
    public const int OpenAIConfidentThreshold = 85;

    /// <summary>
    /// Minimum net confidence required for training data (80%)
    /// Prevents low-quality auto-detections from polluting training dataset
    /// </summary>
    public const int TrainingConfidenceThreshold = 80;

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
