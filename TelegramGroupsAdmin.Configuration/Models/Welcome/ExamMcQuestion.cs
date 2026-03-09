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
