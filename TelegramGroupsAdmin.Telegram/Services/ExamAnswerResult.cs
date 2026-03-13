namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Result of processing an exam answer
/// </summary>
/// <param name="ExamComplete">Whether all questions have been answered</param>
/// <param name="Passed">Whether the user passed (only valid if ExamComplete)</param>
/// <param name="SentToReview">Whether the user was sent to review queue</param>
/// <param name="GroupChatId">The group chat ID where user joined (for welcome response lookup in DM flow)</param>
public record ExamAnswerResult(bool ExamComplete, bool? Passed, bool SentToReview, long? GroupChatId = null);
