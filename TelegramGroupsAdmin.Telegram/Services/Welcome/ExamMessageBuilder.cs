namespace TelegramGroupsAdmin.Telegram.Services.Welcome;

/// <summary>
/// Pure static functions for building exam message text.
/// Ensures preview text matches actual messages sent to users.
/// </summary>
public static class ExamMessageBuilder
{
    /// <summary>
    /// Formats the open-ended question message sent to users in DM.
    /// </summary>
    /// <param name="username">User's formatted mention</param>
    /// <param name="question">The open-ended question text</param>
    /// <returns>Formatted message text</returns>
    public static string FormatOpenEndedQuestion(string username, string question)
    {
        return $"📝 {username}, please answer this question:\n\n{question}\n\nSend your answer below.";
    }

    /// <summary>
    /// Formats the MC question message sent to users in DM.
    /// </summary>
    /// <param name="username">User's formatted mention</param>
    /// <param name="questionNumber">Current question number (1-based)</param>
    /// <param name="totalQuestions">Total number of questions</param>
    /// <param name="questionText">The question text</param>
    /// <returns>Formatted message text</returns>
    public static string FormatMcQuestion(string username, int questionNumber, int totalQuestions, string questionText)
    {
        return $"📝 {username}, Question {questionNumber}/{totalQuestions}:\n\n{questionText}";
    }
}
