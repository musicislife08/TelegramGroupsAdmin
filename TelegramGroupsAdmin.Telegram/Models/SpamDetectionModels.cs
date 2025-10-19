namespace TelegramGroupsAdmin.Telegram.Models;

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
    DateTimeOffset ReportedAt,
    ReportStatus Status,
    string? ReviewedBy,
    DateTimeOffset? ReviewedAt,
    string? ActionTaken,
    string? AdminNotes,
    string? WebUserId = null           // Phase 2.6: Web user ID (always populated for web UI reports)
);

/// <summary>
/// Report review status for spam and impersonation reports
/// </summary>
public enum ReportStatus
{
    /// <summary>Report awaiting admin review</summary>
    Pending = 0,

    /// <summary>Report reviewed and action taken</summary>
    Reviewed = 1,

    /// <summary>Report reviewed and marked as false positive</summary>
    Dismissed = 2
}
