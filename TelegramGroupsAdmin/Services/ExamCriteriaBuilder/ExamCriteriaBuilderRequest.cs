using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Services.ExamCriteriaBuilder;

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
