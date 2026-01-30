using TelegramGroupsAdmin.Telegram.Services.Welcome;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.Welcome;

/// <summary>
/// Test suite for ExamMessageBuilder static methods.
/// Tests pure message formatting logic for exam questions.
/// All tests are pure function tests - no mocks, no Telegram API.
/// </summary>
[TestFixture]
public class ExamMessageBuilderTests
{
    #region FormatMcQuestion Tests

    [Test]
    public void FormatMcQuestion_FormatsCorrectly()
    {
        // Act
        var result = ExamMessageBuilder.FormatMcQuestion(
            username: "@testuser",
            questionNumber: 1,
            totalQuestions: 3,
            questionText: "What is the capital of France?");

        // Assert
        Assert.That(result, Does.Contain("@testuser"));
        Assert.That(result, Does.Contain("Question 1/3"));
        Assert.That(result, Does.Contain("What is the capital of France?"));
    }

    [Test]
    public void FormatMcQuestion_IncludesEmoji()
    {
        // Act
        var result = ExamMessageBuilder.FormatMcQuestion(
            username: "John",
            questionNumber: 2,
            totalQuestions: 4,
            questionText: "Test question");

        // Assert - should have pencil emoji
        Assert.That(result, Does.StartWith("📝"));
    }

    [Test]
    public void FormatMcQuestion_LastQuestion_ShowsCorrectCount()
    {
        // Act
        var result = ExamMessageBuilder.FormatMcQuestion(
            username: "@user",
            questionNumber: 4,
            totalQuestions: 4,
            questionText: "Final question");

        // Assert
        Assert.That(result, Does.Contain("Question 4/4"));
    }

    [Test]
    public void FormatMcQuestion_PreservesSpecialCharactersInQuestion()
    {
        // Act
        var result = ExamMessageBuilder.FormatMcQuestion(
            username: "@user",
            questionNumber: 1,
            totalQuestions: 1,
            questionText: "What is 2 + 2? (Hint: it's < 5 & > 3)");

        // Assert
        Assert.That(result, Does.Contain("What is 2 + 2? (Hint: it's < 5 & > 3)"));
    }

    [Test]
    public void FormatMcQuestion_PreservesEmojiInQuestion()
    {
        // Act
        var result = ExamMessageBuilder.FormatMcQuestion(
            username: "@user",
            questionNumber: 1,
            totalQuestions: 1,
            questionText: "What does 🚀 represent?");

        // Assert
        Assert.That(result, Does.Contain("🚀"));
    }

    #endregion

    #region FormatOpenEndedQuestion Tests

    [Test]
    public void FormatOpenEndedQuestion_FormatsCorrectly()
    {
        // Act
        var result = ExamMessageBuilder.FormatOpenEndedQuestion(
            username: "@testuser",
            question: "Why do you want to join this group?");

        // Assert
        Assert.That(result, Does.Contain("@testuser"));
        Assert.That(result, Does.Contain("Why do you want to join this group?"));
    }

    [Test]
    public void FormatOpenEndedQuestion_IncludesEmoji()
    {
        // Act
        var result = ExamMessageBuilder.FormatOpenEndedQuestion(
            username: "John",
            question: "Test question");

        // Assert - should have pencil emoji
        Assert.That(result, Does.StartWith("📝"));
    }

    [Test]
    public void FormatOpenEndedQuestion_IncludesAnswerInstruction()
    {
        // Act
        var result = ExamMessageBuilder.FormatOpenEndedQuestion(
            username: "@user",
            question: "Describe yourself");

        // Assert - should tell user to send their answer
        Assert.That(result, Does.Contain("Send your answer below"));
    }

    [Test]
    public void FormatOpenEndedQuestion_PreservesMultilineQuestion()
    {
        // Arrange
        var multilineQuestion = "Please answer the following:\n1. Your experience\n2. Your goals";

        // Act
        var result = ExamMessageBuilder.FormatOpenEndedQuestion(
            username: "@user",
            question: multilineQuestion);

        // Assert
        Assert.That(result, Does.Contain("1. Your experience"));
        Assert.That(result, Does.Contain("2. Your goals"));
    }

    [Test]
    public void FormatOpenEndedQuestion_HandlesLongQuestion()
    {
        // Arrange
        var longQuestion = new string('x', 500);

        // Act
        var result = ExamMessageBuilder.FormatOpenEndedQuestion(
            username: "@user",
            question: longQuestion);

        // Assert - should include the full question
        Assert.That(result, Does.Contain(longQuestion));
    }

    #endregion

    #region Edge Cases

    [Test]
    public void FormatMcQuestion_EmptyUsername_IncludesInOutput()
    {
        // Act
        var result = ExamMessageBuilder.FormatMcQuestion(
            username: "",
            questionNumber: 1,
            totalQuestions: 1,
            questionText: "Test");

        // Assert - empty username is caller's responsibility, we just format it
        Assert.That(result, Does.Contain("Question 1/1"));
    }

    [Test]
    public void FormatOpenEndedQuestion_EmptyUsername_IncludesInOutput()
    {
        // Act
        var result = ExamMessageBuilder.FormatOpenEndedQuestion(
            username: "",
            question: "Test");

        // Assert - empty username is caller's responsibility
        Assert.That(result, Does.Contain("Test"));
    }

    [Test]
    public void FormatMcQuestion_EmptyQuestion_IncludesInOutput()
    {
        // Act
        var result = ExamMessageBuilder.FormatMcQuestion(
            username: "@user",
            questionNumber: 1,
            totalQuestions: 1,
            questionText: "");

        // Assert - empty question is caller's responsibility
        Assert.That(result, Does.Contain("@user"));
        Assert.That(result, Does.Contain("Question 1/1"));
    }

    #endregion
}
