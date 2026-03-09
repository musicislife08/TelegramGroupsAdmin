using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Services.ExamCriteriaBuilder;

/// <summary>
/// Service for generating AI-powered evaluation criteria for entrance exam open-ended questions.
/// </summary>
public interface IExamCriteriaBuilderService
{
    /// <summary>
    /// Generate evaluation criteria using AI based on the provided context.
    /// </summary>
    Task<ExamCriteriaBuilderResponse> GenerateCriteriaAsync(
        ExamCriteriaBuilderRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Improve existing criteria based on user feedback.
    /// </summary>
    Task<ExamCriteriaBuilderResponse> ImproveCriteriaAsync(
        string currentCriteria,
        string improvementFeedback,
        ManagedChatRecord? chat = null,
        CancellationToken cancellationToken = default);
}
