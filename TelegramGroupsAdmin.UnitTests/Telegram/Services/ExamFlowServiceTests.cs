using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TelegramGroupsAdmin.Telegram.Services;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services;

/// <summary>
/// Test suite for ExamFlowService pure logic methods.
/// Tests callback parsing and validation logic.
/// Orchestration methods (HandleMcAnswerAsync, etc.) are covered by integration tests.
/// </summary>
[TestFixture]
public class ExamFlowServiceTests
{
    private ExamFlowService _service = null!;

    [SetUp]
    public void SetUp()
    {
        // Create service with mocked dependencies (only needed for constructor)
        var logger = NullLogger<ExamFlowService>.Instance;
        var serviceProvider = Substitute.For<IServiceProvider>();
        var botClientFactory = Substitute.For<ITelegramBotClientFactory>();
        var examEvaluationService = Substitute.For<IExamEvaluationService>();

        _service = new ExamFlowService(
            logger,
            serviceProvider,
            botClientFactory,
            examEvaluationService);
    }

    #region IsExamCallback Tests

    [Test]
    public void IsExamCallback_ValidPrefix_ReturnsTrue()
    {
        // Act & Assert
        Assert.That(_service.IsExamCallback("exam:123:0:1"), Is.True);
    }

    [Test]
    public void IsExamCallback_ExactPrefixOnly_ReturnsTrue()
    {
        // Act & Assert - just the prefix with no data after it
        Assert.That(_service.IsExamCallback("exam:"), Is.True);
    }

    [Test]
    public void IsExamCallback_DifferentPrefix_ReturnsFalse()
    {
        // Act & Assert
        Assert.That(_service.IsExamCallback("welcome:123"), Is.False);
        Assert.That(_service.IsExamCallback("other:data"), Is.False);
    }

    [Test]
    public void IsExamCallback_EmptyString_ReturnsFalse()
    {
        // Act & Assert
        Assert.That(_service.IsExamCallback(""), Is.False);
    }

    [Test]
    public void IsExamCallback_SimilarButNotExactPrefix_ReturnsFalse()
    {
        // Act & Assert - "examination" starts with "exam" but not "exam:"
        Assert.That(_service.IsExamCallback("examination:123"), Is.False);
        Assert.That(_service.IsExamCallback("exam123"), Is.False);
    }

    [Test]
    public void IsExamCallback_CaseSensitive_ReturnsFalse()
    {
        // Act & Assert - prefix is case-sensitive
        Assert.That(_service.IsExamCallback("EXAM:123:0:1"), Is.False);
        Assert.That(_service.IsExamCallback("Exam:123:0:1"), Is.False);
    }

    #endregion

    #region ParseExamCallback Tests

    [Test]
    public void ParseExamCallback_ValidCallback_ReturnsCorrectValues()
    {
        // Act
        var result = _service.ParseExamCallback("exam:12345:2:3");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.SessionId, Is.EqualTo(12345));
        Assert.That(result!.Value.QuestionIndex, Is.EqualTo(2));
        Assert.That(result!.Value.AnswerIndex, Is.EqualTo(3));
    }

    [Test]
    public void ParseExamCallback_ZeroValues_ReturnsCorrectValues()
    {
        // Act
        var result = _service.ParseExamCallback("exam:0:0:0");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.SessionId, Is.EqualTo(0));
        Assert.That(result!.Value.QuestionIndex, Is.EqualTo(0));
        Assert.That(result!.Value.AnswerIndex, Is.EqualTo(0));
    }

    [Test]
    public void ParseExamCallback_LargeSessionId_ReturnsCorrectValue()
    {
        // Act - test with large session ID
        var result = _service.ParseExamCallback("exam:9223372036854775807:0:0");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.SessionId, Is.EqualTo(long.MaxValue));
    }

    [Test]
    public void ParseExamCallback_WrongPrefix_ReturnsNull()
    {
        // Act & Assert
        Assert.That(_service.ParseExamCallback("welcome:123:0:1"), Is.Null);
        Assert.That(_service.ParseExamCallback("other:123:0:1"), Is.Null);
    }

    [Test]
    public void ParseExamCallback_TooFewParts_ReturnsNull()
    {
        // Act & Assert
        Assert.That(_service.ParseExamCallback("exam:123"), Is.Null);
        Assert.That(_service.ParseExamCallback("exam:123:0"), Is.Null);
    }

    [Test]
    public void ParseExamCallback_TooManyParts_ReturnsNull()
    {
        // Act & Assert
        Assert.That(_service.ParseExamCallback("exam:123:0:1:extra"), Is.Null);
    }

    [Test]
    public void ParseExamCallback_NonNumericSessionId_ReturnsNull()
    {
        // Act & Assert
        Assert.That(_service.ParseExamCallback("exam:abc:0:1"), Is.Null);
    }

    [Test]
    public void ParseExamCallback_NonNumericQuestionIndex_ReturnsNull()
    {
        // Act & Assert
        Assert.That(_service.ParseExamCallback("exam:123:abc:1"), Is.Null);
    }

    [Test]
    public void ParseExamCallback_NonNumericAnswerIndex_ReturnsNull()
    {
        // Act & Assert
        Assert.That(_service.ParseExamCallback("exam:123:0:abc"), Is.Null);
    }

    [Test]
    public void ParseExamCallback_NegativeValues_ReturnsCorrectValues()
    {
        // Act - negative values are technically valid parses
        var result = _service.ParseExamCallback("exam:-1:-1:-1");

        // Assert - parsing should succeed (validation is elsewhere)
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.SessionId, Is.EqualTo(-1));
        Assert.That(result!.Value.QuestionIndex, Is.EqualTo(-1));
        Assert.That(result!.Value.AnswerIndex, Is.EqualTo(-1));
    }

    [Test]
    public void ParseExamCallback_EmptyString_ReturnsNull()
    {
        // Act & Assert
        Assert.That(_service.ParseExamCallback(""), Is.Null);
    }

    [Test]
    public void ParseExamCallback_JustPrefix_ReturnsNull()
    {
        // Act & Assert - prefix with no data after colon
        Assert.That(_service.ParseExamCallback("exam:"), Is.Null);
    }

    #endregion
}
