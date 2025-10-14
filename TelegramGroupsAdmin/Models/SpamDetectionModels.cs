namespace TelegramGroupsAdmin.Models;

/// <summary>
/// Training sample for UI display
/// </summary>
public record TrainingSample(
    long Id,
    string MessageText,
    bool IsSpam,
    long AddedDate,
    string Source,
    int? ConfidenceWhenAdded,
    long[] ChatIds,
    string? AddedBy,
    int DetectionCount,
    long? LastDetectedDate
);

/// <summary>
/// Training statistics for UI display
/// </summary>
public record TrainingStats(
    int TotalSamples,
    int SpamSamples,
    int HamSamples,
    double SpamPercentage,
    Dictionary<string, int> SamplesBySource
);

/// <summary>
/// Report record for UI display (user-submitted reports from /report command OR web UI)
/// Phase 2.6: Supports both Telegram /report command and web UI "Flag for Review" button
/// </summary>
public record Report(
    long Id,
    int MessageId,
    long ChatId,
    int? ReportCommandMessageId,      // NULL for web UI reports, populated for Telegram /report
    long? ReportedByUserId,            // NULL if user has no Telegram link, populated if they do
    string? ReportedByUserName,
    long ReportedAt,
    ReportStatus Status,
    string? ReviewedBy,
    long? ReviewedAt,
    string? ActionTaken,
    string? AdminNotes,
    string? WebUserId = null           // Phase 2.6: Web user ID (always populated for web UI reports)
);

/// <summary>
/// Report status enum
/// </summary>
public enum ReportStatus
{
    Pending = 0,
    Reviewed = 1,
    Dismissed = 2
}
