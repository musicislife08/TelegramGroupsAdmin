namespace TelegramGroupsAdmin.Telegram.Repositories;

/// <summary>
/// Repository for managing entrance exam sessions.
/// Tracks user progress through the exam flow until pass/fail/timeout.
/// </summary>
public interface IExamSessionRepository
{
    /// <summary>
    /// Create a new exam session for a user joining a chat.
    /// </summary>
    /// <param name="chatId">The chat where user joined</param>
    /// <param name="userId">The Telegram user ID</param>
    /// <param name="expiresAt">When the session expires (matches welcome timeout)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The created session ID</returns>
    Task<long> CreateSessionAsync(
        long chatId,
        long userId,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get an active exam session for a user in a chat.
    /// Returns null if no active session exists.
    /// </summary>
    Task<ExamSession?> GetSessionAsync(
        long chatId,
        long userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get an exam session by ID.
    /// </summary>
    Task<ExamSession?> GetByIdAsync(
        long sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Record a multiple choice answer.
    /// Updates mc_answers JSONB and increments current_question_index.
    /// </summary>
    /// <param name="sessionId">Session ID</param>
    /// <param name="questionIndex">Question index (0-based)</param>
    /// <param name="answer">Selected answer (A, B, C, D)</param>
    /// <param name="shuffleState">Answer shuffle mapping for this question</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task RecordMcAnswerAsync(
        long sessionId,
        int questionIndex,
        string answer,
        int[]? shuffleState = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Record the open-ended answer.
    /// </summary>
    Task RecordOpenEndedAnswerAsync(
        long sessionId,
        string answer,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a session (on pass, fail, or user leave).
    /// </summary>
    Task DeleteSessionAsync(
        long sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete session by chat and user (for cleanup when user leaves).
    /// </summary>
    Task DeleteSessionAsync(
        long chatId,
        long userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete expired sessions (for cleanup job).
    /// Returns count of deleted sessions.
    /// </summary>
    Task<int> DeleteExpiredSessionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a user has an active exam session in a chat.
    /// </summary>
    Task<bool> HasActiveSessionAsync(
        long chatId,
        long userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get an active session for a user across any group chat.
    /// Used to find session when user sends a DM (open-ended answer).
    /// </summary>
    /// <param name="userId">The Telegram user ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Active session if found, null otherwise</returns>
    Task<ExamSession?> GetActiveSessionForUserAsync(
        long userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all active sessions for a chat (admin visibility).
    /// </summary>
    Task<List<ExamSession>> GetActiveSessionsAsync(
        long chatId,
        CancellationToken cancellationToken = default);
}

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
