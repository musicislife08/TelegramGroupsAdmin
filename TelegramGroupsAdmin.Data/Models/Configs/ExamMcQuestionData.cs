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
