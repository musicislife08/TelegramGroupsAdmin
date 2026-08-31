using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;

namespace TelegramGroupsAdmin.Telegram.Services.Welcome;

/// <summary>
/// Pure static functions for building exam messages as entity-based <see cref="TelegramMessage"/>s.
/// The user reference is a clickable <c>text_mention</c> (works without a username), and the same
/// builder output drives both the live DM and the config-editor preview, so the preview matches
/// exactly what is sent.
/// </summary>
public static class ExamMessageBuilder
{
    /// <summary>
    /// Builds the open-ended question message sent to users in DM.
    /// </summary>
    public static TelegramMessage FormatOpenEndedQuestion(UserIdentity user, string question)
        => new TelegramMessageBuilder()
            .Text("📝 ")
            .Mention(user)
            .Text($", please answer this question:\n\n{question}\n\nSend your answer below.")
            .Build();

    /// <summary>
    /// Builds the MC question message sent to users in DM.
    /// </summary>
    public static TelegramMessage FormatMcQuestion(UserIdentity user, int questionNumber, int totalQuestions, string questionText)
        => new TelegramMessageBuilder()
            .Text("📝 ")
            .Mention(user)
            .Text($", Question {questionNumber}/{totalQuestions}:\n\n{questionText}")
            .Build();
}
