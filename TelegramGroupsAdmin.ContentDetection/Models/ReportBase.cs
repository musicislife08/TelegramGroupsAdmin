namespace TelegramGroupsAdmin.ContentDetection.Models;

/// <summary>
/// Base report model for generic operations across all report types.
/// Used when the caller doesn't need type-specific context.
/// </summary>
public record ReportBase
{
    public long Id { get; init; }
    public ReportType Type { get; init; }
    public long ChatId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public ReportStatus Status { get; init; }
    public string? ReviewedBy { get; init; }
    public DateTimeOffset? ReviewedAt { get; init; }
    public string? ActionTaken { get; init; }
    public string? AdminNotes { get; init; }

    /// <summary>
    /// Raw context JSON for type-specific data.
    /// Callers needing typed access should use type-specific repository methods.
    /// </summary>
    public string? Context { get; init; }

    // Denormalized for display (joined data - optional)
    public string? ChatName { get; init; }

    /// <summary>
    /// For ImpersonationAlert: the suspected user ID
    /// For ContentReport: the message author user ID
    /// For ExamFailure: the user who failed the exam
    /// </summary>
    public long? SubjectUserId { get; init; }

    // Common ContentReport-specific fields (hydrated from base columns)
    /// <summary>Message ID for ContentReport type</summary>
    public int? MessageId { get; init; }

    /// <summary>ID of the /report command message (for cleanup)</summary>
    public int? ReportCommandMessageId { get; init; }

    /// <summary>User ID of the reported user (alias for SubjectUserId for ContentReport type)</summary>
    public long? ReportedUserId => SubjectUserId;
}
