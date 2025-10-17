namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Unified model for reports queue (combines spam reports and impersonation alerts)
/// Enables card-based UI that displays both types in a single chronological stream
/// </summary>
public record ReportQueueItem
{
    public required ReportType Type { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required int Priority { get; init; } // Higher = more urgent
    public required bool IsPending { get; init; }

    // Either Report OR ImpersonationAlertRecord is populated
    public Report? SpamReport { get; init; }
    public ImpersonationAlertRecord? ImpersonationAlert { get; init; }

    // Common display properties
    public required string DisplayTitle { get; init; }
    public required long UserId { get; init; }
    public required string? UserName { get; init; }
}

/// <summary>
/// Type of report in the unified queue
/// </summary>
public enum ReportType
{
    Spam = 0,
    Impersonation = 1
}
