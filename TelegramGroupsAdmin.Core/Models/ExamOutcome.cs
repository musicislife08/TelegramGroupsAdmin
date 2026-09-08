namespace TelegramGroupsAdmin.Core.Models;

/// <summary>
/// Final outcome of a completed entrance exam.
/// Serialized as int in the report JSONB context — never as a name.
/// </summary>
public enum ExamOutcome
{
    /// <summary>Exam failed (or AI unavailable) — pending admin review</summary>
    Failed = 0,

    /// <summary>Exam passed — user auto-admitted, record born completed</summary>
    Passed = 1
}
