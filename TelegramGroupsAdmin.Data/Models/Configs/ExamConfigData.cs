namespace TelegramGroupsAdmin.Data.Models.Configs;

/// <summary>
/// Data layer representation of ExamMcQuestion for EF Core JSON column mapping.
/// </summary>
public class ExamMcQuestionData
{
    /// <summary>The question text</summary>
    public string Question { get; set; } = string.Empty;

    /// <summary>
    /// Answer options. First answer is always the correct one.
    /// </summary>
    public List<string> Answers { get; set; } = [];
}

/// <summary>
/// Data layer representation of ExamConfig for EF Core JSON column mapping.
/// </summary>
public class ExamConfigData
{
    /// <summary>
    /// Multiple-choice questions (optional, 0-4 questions).
    /// </summary>
    public List<ExamMcQuestionData> McQuestions { get; set; } = [];

    /// <summary>
    /// Minimum percentage of MC questions that must be correct (0-100).
    /// </summary>
    public int McPassingThreshold { get; set; } = 80;

    /// <summary>
    /// Open-ended question text (optional).
    /// </summary>
    public string? OpenEndedQuestion { get; set; }

    /// <summary>
    /// Context about what the group is about (used by AI for evaluation).
    /// </summary>
    public string? GroupTopic { get; set; }

    /// <summary>
    /// AI-generated evaluation criteria for open-ended answers.
    /// </summary>
    public string? EvaluationCriteria { get; set; }

    /// <summary>
    /// Whether to require both MC pass AND open-ended pass.
    /// </summary>
    public bool RequireBothToPass { get; set; } = true;
}
