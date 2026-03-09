namespace TelegramGroupsAdmin.Services.ExamCriteriaBuilder;

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
