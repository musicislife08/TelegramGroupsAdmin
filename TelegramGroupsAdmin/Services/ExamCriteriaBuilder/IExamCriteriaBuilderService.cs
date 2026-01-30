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

/// <summary>
/// Request model for generating exam evaluation criteria.
/// </summary>
public class ExamCriteriaBuilderRequest
{
    /// <summary>
    /// The chat this configuration is for. Used for logging context.
    /// Null for global/default configuration.
    /// </summary>
    public ManagedChatRecord? Chat { get; set; }

    /// <summary>
    /// The open-ended question being asked.
    /// </summary>
    public string Question { get; set; } = string.Empty;

    /// <summary>
    /// What the group is about (context for AI).
    /// </summary>
    public string GroupTopic { get; set; } = string.Empty;

    /// <summary>
    /// What makes a good answer (admin hints).
    /// </summary>
    public string? GoodAnswerHints { get; set; }

    /// <summary>
    /// What should fail (red flags).
    /// </summary>
    public string? FailureIndicators { get; set; }

    /// <summary>
    /// Strictness level for evaluation.
    /// </summary>
    public ExamStrictnessLevel Strictness { get; set; } = ExamStrictnessLevel.Balanced;
}

/// <summary>
/// Response model from criteria generation.
/// </summary>
public class ExamCriteriaBuilderResponse
{
    public bool Success { get; set; }
    public string? GeneratedCriteria { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Chat display name for snackbar notifications.
    /// </summary>
    public string? ChatDisplayName { get; set; }
}

/// <summary>
/// Strictness level for exam evaluation.
/// </summary>
public enum ExamStrictnessLevel
{
    /// <summary>
    /// Lenient - Accept most answers that show any effort.
    /// </summary>
    Lenient,

    /// <summary>
    /// Balanced - Require reasonable effort and relevance.
    /// </summary>
    Balanced,

    /// <summary>
    /// Strict - Require high quality, detailed answers.
    /// </summary>
    Strict
}
