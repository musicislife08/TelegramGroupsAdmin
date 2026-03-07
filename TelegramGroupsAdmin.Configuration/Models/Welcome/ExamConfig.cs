namespace TelegramGroupsAdmin.Configuration.Models.Welcome;

/// <summary>
/// A multiple-choice question for the entrance exam
/// </summary>
public class ExamMcQuestion
{
    /// <summary>The question text</summary>
    public string Question { get; set; } = string.Empty;

    /// <summary>
    /// Answer options. First answer is always the correct one.
    /// Options are shuffled when displayed to user.
    /// Minimum 2, maximum 4 answers.
    /// </summary>
    public List<string> Answers { get; set; } = [];
}

/// <summary>
/// Configuration for the entrance exam welcome mode
/// </summary>
public class ExamConfig
{
    /// <summary>
    /// Multiple-choice questions (optional, 0-4 questions).
    /// If empty, only open-ended question is used.
    /// </summary>
    public List<ExamMcQuestion> McQuestions { get; set; } = [];

    /// <summary>
    /// Minimum percentage of MC questions that must be correct (0-100).
    /// Default: 80%
    /// </summary>
    public int McPassingThreshold { get; set; } = 80;

    /// <summary>
    /// Open-ended question text (optional).
    /// If empty, only MC questions are used.
    /// </summary>
    public string? OpenEndedQuestion { get; set; }

    /// <summary>
    /// Context about what the group is about (used by AI for evaluation).
    /// Required if OpenEndedQuestion is set.
    /// </summary>
    public string? GroupTopic { get; set; }

    /// <summary>
    /// AI-generated evaluation criteria for open-ended answers.
    /// Created via prompt builder UI, stored as plain text.
    /// </summary>
    public string? EvaluationCriteria { get; set; }

    /// <summary>
    /// Whether to require both MC pass AND open-ended pass.
    /// If false, passing either is sufficient (when both configured).
    /// Default: true (both must pass)
    /// </summary>
    public bool RequireBothToPass { get; set; } = true;

    /// <summary>
    /// Check if exam has any questions configured
    /// </summary>
    public bool HasMcQuestions => McQuestions.Count > 0;

    /// <summary>
    /// Check if exam has open-ended question configured
    /// </summary>
    public bool HasOpenEndedQuestion => !string.IsNullOrWhiteSpace(OpenEndedQuestion);

    /// <summary>
    /// Check if exam is properly configured (at least one question type)
    /// </summary>
    public bool IsValid => HasMcQuestions || HasOpenEndedQuestion;
}
