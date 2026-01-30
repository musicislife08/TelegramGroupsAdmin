using TelegramGroupsAdmin.Configuration.Models.Welcome;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Models;

/// <summary>
/// Test suite for ExamConfig model validation properties.
/// Tests computed properties that determine exam validity.
/// </summary>
[TestFixture]
public class ExamConfigTests
{
    #region HasMcQuestions Tests

    [Test]
    public void HasMcQuestions_WithQuestions_ReturnsTrue()
    {
        // Arrange
        var config = new ExamConfig
        {
            McQuestions =
            [
                new ExamMcQuestion
                {
                    Question = "Test question",
                    Answers = ["A", "B", "C"]
                }
            ]
        };

        // Act & Assert
        Assert.That(config.HasMcQuestions, Is.True);
    }

    [Test]
    public void HasMcQuestions_EmptyList_ReturnsFalse()
    {
        // Arrange
        var config = new ExamConfig
        {
            McQuestions = []
        };

        // Act & Assert
        Assert.That(config.HasMcQuestions, Is.False);
    }

    [Test]
    public void HasMcQuestions_DefaultConfig_ReturnsFalse()
    {
        // Arrange
        var config = new ExamConfig();

        // Act & Assert
        Assert.That(config.HasMcQuestions, Is.False);
    }

    [Test]
    public void HasMcQuestions_MultipleQuestions_ReturnsTrue()
    {
        // Arrange
        var config = new ExamConfig
        {
            McQuestions =
            [
                new ExamMcQuestion { Question = "Q1", Answers = ["A", "B"] },
                new ExamMcQuestion { Question = "Q2", Answers = ["A", "B"] },
                new ExamMcQuestion { Question = "Q3", Answers = ["A", "B"] },
                new ExamMcQuestion { Question = "Q4", Answers = ["A", "B"] }
            ]
        };

        // Act & Assert
        Assert.That(config.HasMcQuestions, Is.True);
        Assert.That(config.McQuestions.Count, Is.EqualTo(4));
    }

    #endregion

    #region HasOpenEndedQuestion Tests

    [Test]
    public void HasOpenEndedQuestion_WithQuestion_ReturnsTrue()
    {
        // Arrange
        var config = new ExamConfig
        {
            OpenEndedQuestion = "Why do you want to join?"
        };

        // Act & Assert
        Assert.That(config.HasOpenEndedQuestion, Is.True);
    }

    [Test]
    public void HasOpenEndedQuestion_NullQuestion_ReturnsFalse()
    {
        // Arrange
        var config = new ExamConfig
        {
            OpenEndedQuestion = null
        };

        // Act & Assert
        Assert.That(config.HasOpenEndedQuestion, Is.False);
    }

    [Test]
    public void HasOpenEndedQuestion_EmptyQuestion_ReturnsFalse()
    {
        // Arrange
        var config = new ExamConfig
        {
            OpenEndedQuestion = ""
        };

        // Act & Assert
        Assert.That(config.HasOpenEndedQuestion, Is.False);
    }

    [Test]
    public void HasOpenEndedQuestion_WhitespaceOnly_ReturnsFalse()
    {
        // Arrange
        var config = new ExamConfig
        {
            OpenEndedQuestion = "   "
        };

        // Act & Assert
        Assert.That(config.HasOpenEndedQuestion, Is.False);
    }

    [Test]
    public void HasOpenEndedQuestion_DefaultConfig_ReturnsFalse()
    {
        // Arrange
        var config = new ExamConfig();

        // Act & Assert
        Assert.That(config.HasOpenEndedQuestion, Is.False);
    }

    #endregion

    #region IsValid Tests

    [Test]
    public void IsValid_OnlyMcQuestions_ReturnsTrue()
    {
        // Arrange
        var config = new ExamConfig
        {
            McQuestions =
            [
                new ExamMcQuestion { Question = "Q1", Answers = ["A", "B"] }
            ]
        };

        // Act & Assert
        Assert.That(config.IsValid, Is.True);
    }

    [Test]
    public void IsValid_OnlyOpenEnded_ReturnsTrue()
    {
        // Arrange
        var config = new ExamConfig
        {
            OpenEndedQuestion = "Why do you want to join?"
        };

        // Act & Assert
        Assert.That(config.IsValid, Is.True);
    }

    [Test]
    public void IsValid_BothQuestionTypes_ReturnsTrue()
    {
        // Arrange
        var config = new ExamConfig
        {
            McQuestions =
            [
                new ExamMcQuestion { Question = "Q1", Answers = ["A", "B"] }
            ],
            OpenEndedQuestion = "Why do you want to join?"
        };

        // Act & Assert
        Assert.That(config.IsValid, Is.True);
    }

    [Test]
    public void IsValid_NoQuestions_ReturnsFalse()
    {
        // Arrange
        var config = new ExamConfig
        {
            McQuestions = [],
            OpenEndedQuestion = null
        };

        // Act & Assert
        Assert.That(config.IsValid, Is.False);
    }

    [Test]
    public void IsValid_DefaultConfig_ReturnsFalse()
    {
        // Arrange
        var config = new ExamConfig();

        // Act & Assert
        Assert.That(config.IsValid, Is.False);
    }

    #endregion

    #region Default Values Tests

    [Test]
    public void DefaultConfig_HasCorrectDefaults()
    {
        // Arrange
        var config = new ExamConfig();

        // Assert
        Assert.That(config.McPassingThreshold, Is.EqualTo(80));
        Assert.That(config.RequireBothToPass, Is.True);
        Assert.That(config.McQuestions, Is.Empty);
        Assert.That(config.OpenEndedQuestion, Is.Null);
        Assert.That(config.GroupTopic, Is.Null);
        Assert.That(config.EvaluationCriteria, Is.Null);
    }

    #endregion
}
