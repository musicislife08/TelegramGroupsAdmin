using Telegram.Bot.Types;
using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Result of starting an exam session
/// </summary>
/// <param name="Success">Whether the exam started successfully</param>
/// <param name="WelcomeMessageId">ID of the first exam message sent</param>
public record ExamStartResult(bool Success, int WelcomeMessageId);

/// <summary>
/// Result of processing an exam answer
/// </summary>
/// <param name="ExamComplete">Whether all questions have been answered</param>
/// <param name="Passed">Whether the user passed (only valid if ExamComplete)</param>
/// <param name="SentToReview">Whether the user was sent to review queue</param>
public record ExamAnswerResult(bool ExamComplete, bool? Passed, bool SentToReview);

/// <summary>
/// Service for managing entrance exam flow.
/// Handles MC question display, answer validation, open-ended evaluation.
/// </summary>
public interface IExamFlowService
{
    /// <summary>
    /// Start an exam session for a new user
    /// </summary>
    /// <param name="chat">The chat the user joined</param>
    /// <param name="user">The user taking the exam</param>
    /// <param name="config">Welcome config with exam settings</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result with welcome message ID</returns>
    Task<ExamStartResult> StartExamAsync(
        Chat chat,
        User user,
        WelcomeConfig config,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Handle an MC answer callback
    /// </summary>
    /// <param name="sessionId">Exam session ID</param>
    /// <param name="questionIndex">Index of the question being answered</param>
    /// <param name="answerIndex">Index of the selected answer (in shuffled order)</param>
    /// <param name="user">User who clicked the button</param>
    /// <param name="message">The message with the question</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result indicating exam status</returns>
    Task<ExamAnswerResult> HandleMcAnswerAsync(
        long sessionId,
        int questionIndex,
        int answerIndex,
        User user,
        Message message,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Handle an open-ended answer (reply to bot's question)
    /// </summary>
    /// <param name="chatId">Chat ID</param>
    /// <param name="user">User who replied</param>
    /// <param name="answerText">The user's answer text</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result indicating exam status</returns>
    Task<ExamAnswerResult> HandleOpenEndedAnswerAsync(
        long chatId,
        User user,
        string answerText,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a user has an active exam session in a chat
    /// </summary>
    Task<bool> HasActiveSessionAsync(long chatId, long userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancel an exam session (e.g., user left chat)
    /// </summary>
    Task CancelSessionAsync(long chatId, long userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if callback data is an exam callback
    /// </summary>
    bool IsExamCallback(string callbackData);

    /// <summary>
    /// Parse exam callback data
    /// </summary>
    /// <returns>Tuple of (sessionId, questionIndex, answerIndex) or null if invalid</returns>
    (long SessionId, int QuestionIndex, int AnswerIndex)? ParseExamCallback(string callbackData);
}
