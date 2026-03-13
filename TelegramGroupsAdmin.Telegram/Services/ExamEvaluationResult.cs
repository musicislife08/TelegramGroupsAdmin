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
