using NUnit.Framework;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Services.Welcome;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.Welcome;

/// <summary>
/// Tests for ExamMessageBuilder — entity-based messages whose text_mention drives both the live DM
/// and the config-editor preview. The mention display name comes from TelegramDisplayName.Format
/// (first/last name, no @), so a user named "Test" renders as "Test".
/// </summary>
[TestFixture]
public class ExamMessageBuilderTests
{
    private static readonly UserIdentity TestUser = new(123, "Test", null, "testuser");

    [Test]
    public void FormatOpenEndedQuestion_MentionsUser_AndIncludesQuestion()
    {
        var result = ExamMessageBuilder.FormatOpenEndedQuestion(TestUser, "What is your favorite color?", NameMasking.Off);

        Assert.That(result.Text, Does.Contain("Test"));
        Assert.That(result.Text, Does.Contain("What is your favorite color?"));
        Assert.That(result.Entities, Has.Some.Matches<MessageEntity>(
            e => e.Type == MessageEntityType.TextMention && e.User!.Id == 123));
    }

    [Test]
    public void FormatOpenEndedQuestion_IncludesInstructions()
    {
        var result = ExamMessageBuilder.FormatOpenEndedQuestion(TestUser, "What is your favorite color?", NameMasking.Off);

        Assert.That(result.Text, Does.Contain("please answer this question"));
    }

    [Test]
    public void FormatMcQuestion_MentionsUser_AsTextMention()
    {
        var result = ExamMessageBuilder.FormatMcQuestion(TestUser, 1, 3, "What is 2+2?", NameMasking.Off);

        Assert.That(result.Text, Does.Contain("Test"));
        Assert.That(result.Entities, Has.Some.Matches<MessageEntity>(
            e => e.Type == MessageEntityType.TextMention && e.User!.Id == 123));
    }

    [Test]
    public void FormatMcQuestion_IncludesQuestionNumber()
    {
        var result = ExamMessageBuilder.FormatMcQuestion(TestUser, 1, 3, "What is 2+2?", NameMasking.Off);

        Assert.That(result.Text, Does.Contain("1/3"));
    }

    [Test]
    public void FormatMcQuestion_IncludesQuestionText()
    {
        var result = ExamMessageBuilder.FormatMcQuestion(TestUser, 1, 3, "What is 2+2?", NameMasking.Off);

        Assert.That(result.Text, Does.Contain("What is 2+2?"));
    }

    [Test]
    public void FormatMcQuestion_DifferentNumbers()
    {
        var result = ExamMessageBuilder.FormatMcQuestion(TestUser, 2, 5, "Question text", NameMasking.Off);

        Assert.That(result.Text, Does.Contain("2/5"));
    }

    [Test]
    public void FormatOpenEndedQuestion_DifferentQuestions()
    {
        var result = ExamMessageBuilder.FormatOpenEndedQuestion(TestUser, "Different question?", NameMasking.Off);

        Assert.That(result.Text, Does.Contain("Different question?"));
    }

    [Test]
    public void FormatMcQuestion_FirstQuestion()
    {
        var result = ExamMessageBuilder.FormatMcQuestion(TestUser, 1, 1, "Only question", NameMasking.Off);

        Assert.That(result.Text, Does.Contain("1/1"));
    }

    [Test]
    public void FormatMcQuestion_UsernamelessUser_StillClickableViaTextMention()
    {
        var noUsername = new UserIdentity(999, "NoUser", null, null);

        var result = ExamMessageBuilder.FormatMcQuestion(noUsername, 1, 1, "Q", NameMasking.Off);

        Assert.That(result.Entities, Has.Some.Matches<MessageEntity>(
            e => e.Type == MessageEntityType.TextMention && e.User!.Id == 999));
    }

    [Test]
    public void FormatMcQuestion_ExplicitUserWithMaskingOn_ShowsLabelAsMention()
    {
        var user = UserIdentity.ForTest(7, "Bad", verdict: NameVerdict.Explicit);

        var result = ExamMessageBuilder.FormatMcQuestion(user, 1, 1, "Q", NameMasking.On);

        Assert.That(result.Text, Does.StartWith("📝 [name removed: explicit],"));
        Assert.That(result.Entities.Single().User!.Id, Is.EqualTo(7));
    }

    [Test]
    public void FormatOpenEndedQuestion_ExplicitUserWithMaskingOn_ShowsLabel()
    {
        var user = UserIdentity.ForTest(7, "Bad", verdict: NameVerdict.Explicit);

        var result = ExamMessageBuilder.FormatOpenEndedQuestion(user, "Why?", NameMasking.On);

        Assert.That(result.Text, Does.StartWith("📝 [name removed: explicit],"));
    }
}
