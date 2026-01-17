namespace TelegramGroupsAdmin.ContentDetection.Models;

/// <summary>
/// Domain model for exam failure reviews.
/// Created when a user fails the entrance exam and needs admin review.
/// </summary>
public record ExamFailureRecord
{
    public long Id { get; init; }
    public long ChatId { get; init; }
    public long UserId { get; init; }

    /// <summary>
    /// User's multiple choice answers.
    /// Key = question index, Value = selected answer letter (A, B, C, D)
    /// </summary>
    public Dictionary<int, string>? McAnswers { get; init; }

    /// <summary>
    /// Shuffle state for each question (maps display position to original answer index).
    /// Key = question index, Value = array of original indices in display order.
    /// Example: [2, 0, 1, 3] means position 0 (A) shows original answer 2, position 1 (B) shows original answer 0 (correct), etc.
    /// </summary>
    public Dictionary<int, int[]>? ShuffleState { get; init; }

    /// <summary>
    /// User's response to the open-ended question (if configured).
    /// </summary>
    public string? OpenEndedAnswer { get; init; }

    /// <summary>
    /// Percentage score achieved (0-100).
    /// </summary>
    public int Score { get; init; }

    /// <summary>
    /// Passing threshold that was configured (0-100).
    /// </summary>
    public int PassingThreshold { get; init; }

    /// <summary>
    /// AI evaluation result for open-ended question (if applicable).
    /// </summary>
    public string? AiEvaluation { get; init; }

    /// <summary>
    /// When the exam was completed and failed.
    /// </summary>
    public DateTimeOffset FailedAt { get; init; }

    /// <summary>
    /// Admin review status and metadata.
    /// </summary>
    public string? ReviewedBy { get; init; }
    public DateTimeOffset? ReviewedAt { get; init; }
    public string? ActionTaken { get; init; }
    public string? AdminNotes { get; init; }

    // Denormalized for display (joined data)
    public string? UserName { get; init; }
    public string? UserFirstName { get; init; }
    public string? UserLastName { get; init; }
    public string? UserPhotoPath { get; init; }
    public string? ChatName { get; init; }
}
