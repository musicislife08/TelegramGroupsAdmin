namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Result of evaluating an open-ended exam answer
/// </summary>
/// <param name="Passed">Whether the answer meets the criteria</param>
/// <param name="Reasoning">AI's explanation for the decision</param>
/// <param name="Confidence">Confidence score (0.0-1.0)</param>
public record ExamEvaluationResult(
    bool Passed,
    string Reasoning,
    double Confidence);

/// <summary>
/// Service for evaluating open-ended entrance exam answers using AI.
/// Uses the same AI connection as content moderation (SpamDetection feature).
/// </summary>
public interface IExamEvaluationService
{
    /// <summary>
    /// Evaluate a user's open-ended exam answer against configured criteria
    /// </summary>
    /// <param name="question">The question that was asked</param>
    /// <param name="userAnswer">The user's answer text</param>
    /// <param name="evaluationCriteria">AI-generated criteria for pass/fail (from exam config)</param>
    /// <param name="groupTopic">Context about what the group is about</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Evaluation result with pass/fail, reasoning, and confidence; null if AI unavailable</returns>
    Task<ExamEvaluationResult?> EvaluateAnswerAsync(
        string question,
        string userAnswer,
        string evaluationCriteria,
        string groupTopic,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if exam evaluation is available (AI connection configured)
    /// </summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);
}
