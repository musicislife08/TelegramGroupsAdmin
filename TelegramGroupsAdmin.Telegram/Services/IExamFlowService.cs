using Telegram.Bot.Types;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Services.Moderation;

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
/// <param name="GroupChatId">The group chat ID where user joined (for welcome response lookup in DM flow)</param>
public record ExamAnswerResult(bool ExamComplete, bool? Passed, bool SentToReview, long? GroupChatId = null);

/// <summary>
/// Context for an active exam session, including whether it's awaiting an open-ended answer.
/// </summary>
/// <param name="GroupChatId">The group chat where user joined</param>
/// <param name="AwaitingOpenEndedAnswer">True if MC questions are complete and open-ended is pending</param>
public record ActiveExamContext(long GroupChatId, bool AwaitingOpenEndedAnswer);

/// <summary>
/// Service for managing entrance exam flow.
/// Handles MC question display, answer validation, open-ended evaluation.
/// </summary>
public interface IExamFlowService
{
    /// <summary>
    /// Start an exam session for a new user.
    /// Note: This sends questions to the group chat. Consider using StartExamInDmAsync instead.
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
    /// Start an exam session in user's DM (triggered via deep link)
    /// </summary>
    /// <param name="chat">The group chat where user joined</param>
    /// <param name="user">The user taking the exam</param>
    /// <param name="dmChatId">User's private chat with bot (where questions are sent)</param>
    /// <param name="config">Welcome config with exam settings</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result with first question message ID</returns>
    Task<ExamStartResult> StartExamInDmAsync(
        ChatIdentity chat,
        User user,
        long dmChatId,
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
    Task<bool> HasActiveSessionAsync(ChatIdentity chat, UserIdentity user, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get an active exam session for a user (across any group chat).
    /// Used to find session when user sends a DM with open-ended answer.
    /// </summary>
    /// <param name="user">The Telegram user</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Active session with exam config context, or null if none</returns>
    Task<ActiveExamContext?> GetActiveExamContextAsync(UserIdentity user, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancel an exam session (e.g., user left chat)
    /// </summary>
    Task CancelSessionAsync(ChatIdentity chat, UserIdentity user, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if callback data is an exam callback
    /// </summary>
    bool IsExamCallback(string callbackData);

    /// <summary>
    /// Parse exam callback data
    /// </summary>
    /// <returns>Tuple of (sessionId, questionIndex, answerIndex) or null if invalid</returns>
    (long SessionId, int QuestionIndex, int AnswerIndex)? ParseExamCallback(string callbackData);

    /// <summary>
    /// Approve an exam failure after admin review.
    /// Restores user permissions, deletes teaser message, updates welcome response.
    /// </summary>
    /// <param name="user">Identity of the user being approved</param>
    /// <param name="chat">Identity of the chat where the exam was taken</param>
    /// <param name="examFailureId">ID of the exam failure record</param>
    /// <param name="executor">Actor performing the approval</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result indicating success or failure</returns>
    Task<ModerationResult> ApproveExamFailureAsync(
        UserIdentity user,
        ChatIdentity chat,
        long examFailureId,
        Actor executor,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deny an exam failure after admin review (kick user, allow rejoin).
    /// Kicks user from chat, deletes teaser message, updates welcome response, sends notification.
    /// </summary>
    /// <param name="user">Identity of the user being denied</param>
    /// <param name="chat">Identity of the chat where the exam was taken</param>
    /// <param name="executor">Actor performing the denial</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result indicating success or failure</returns>
    Task<ModerationResult> DenyExamFailureAsync(
        UserIdentity user,
        ChatIdentity chat,
        Actor executor,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deny an exam failure after admin review and ban the user globally.
    /// Bans user from all managed chats, deletes teaser message, updates welcome response, sends notification.
    /// </summary>
    /// <param name="user">Identity of the user being denied and banned</param>
    /// <param name="chat">Identity of the chat where the exam was taken</param>
    /// <param name="executor">Actor performing the denial</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result indicating success or failure</returns>
    Task<ModerationResult> DenyAndBanExamFailureAsync(
        UserIdentity user,
        ChatIdentity chat,
        Actor executor,
        CancellationToken cancellationToken = default);
}
