namespace TelegramGroupsAdmin.Telegram.Repositories;

/// <summary>
/// Domain model for exam session (UI layer).
/// </summary>
public record ExamSession
{
    public long Id { get; init; }
    public long ChatId { get; init; }
    public long UserId { get; init; }
    public int CurrentQuestionIndex { get; init; }

    /// <summary>
    /// User's multiple choice answers.
    /// Key = question index, Value = selected answer letter (A, B, C, D)
    /// </summary>
    public Dictionary<int, string>? McAnswers { get; init; }

    /// <summary>
    /// Answer shuffle state for each question.
    /// Key = question index, Value = display order array (e.g., [2,0,1,3] means answer at index 0 displays as 3rd option)
    /// </summary>
    public Dictionary<int, int[]>? ShuffleState { get; init; }

    public string? OpenEndedAnswer { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }

    /// <summary>
    /// Check if session has expired.
    /// </summary>
    public bool IsExpired => DateTimeOffset.UtcNow > ExpiresAt;
}
