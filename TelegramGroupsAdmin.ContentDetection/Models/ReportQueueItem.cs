namespace TelegramGroupsAdmin.ContentDetection.Models;

/// <summary>
/// Unified model for reports queue (combines spam reports, impersonation alerts, and exam failures)
/// Enables card-based UI that displays all review types in a single chronological stream
/// </summary>
public record ReportQueueItem
{
    public required ReportType Type { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required int Priority { get; init; } // Higher = more urgent
    public required bool IsPending { get; init; }

    // One of Report, ImpersonationAlertRecord, or ExamFailureRecord is populated
    public Report? SpamReport { get; init; }
    public ImpersonationAlertRecord? ImpersonationAlert { get; init; }
    public ExamFailureRecord? ExamFailure { get; init; }

    // Common display properties
    public required string DisplayTitle { get; init; }
    public required long UserId { get; init; }
    public required string? UserName { get; init; }
}
