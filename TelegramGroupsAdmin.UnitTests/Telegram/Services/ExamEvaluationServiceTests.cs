using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Core.Services.AI;
using TelegramGroupsAdmin.Telegram.Services;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services;

/// <summary>
/// Test suite for ExamEvaluationService.
/// Tests AI availability checks, answer evaluation, and response parsing.
/// </summary>
[TestFixture]
public class ExamEvaluationServiceTests
{
    private ExamEvaluationService _service = null!;
    private IChatService _mockChatService = null!;

    [SetUp]
    public void SetUp()
    {
        _mockChatService = Substitute.For<IChatService>();
        var logger = NullLogger<ExamEvaluationService>.Instance;

        _service = new ExamEvaluationService(_mockChatService, logger);
    }

    #region IsAvailableAsync Tests

    [Test]
    public async Task IsAvailableAsync_WhenAIConfigured_ReturnsTrue()
    {
        // Arrange
        _mockChatService
            .IsFeatureAvailableAsync(AIFeatureType.SpamDetection, Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        var result = await _service.IsAvailableAsync();

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public async Task IsAvailableAsync_WhenAINotConfigured_ReturnsFalse()
    {
        // Arrange
        _mockChatService
            .IsFeatureAvailableAsync(AIFeatureType.SpamDetection, Arg.Any<CancellationToken>())
            .Returns(false);

        // Act
        var result = await _service.IsAvailableAsync();

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task IsAvailableAsync_UsesSpamDetectionFeatureType()
    {
        // Arrange & Act
        await _service.IsAvailableAsync();

        // Assert - verifies it uses SpamDetection feature type (reused for exams)
        await _mockChatService.Received(1)
            .IsFeatureAvailableAsync(AIFeatureType.SpamDetection, Arg.Any<CancellationToken>());
    }

    #endregion

    #region EvaluateAnswerAsync - Empty/Null Input Tests

    [Test]
    public async Task EvaluateAnswerAsync_EmptyAnswer_ReturnsFailedWithFullConfidence()
    {
        // Act
        var result = await _service.EvaluateAnswerAsync(
            question: "What is your interest?",
            userAnswer: "",
            evaluationCriteria: "Must show interest",
            groupTopic: "Tech Discussion");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Passed, Is.False);
        Assert.That(result.Reasoning, Is.EqualTo("No answer provided"));
        Assert.That(result.Confidence, Is.EqualTo(1.0));
    }

    [Test]
    public async Task EvaluateAnswerAsync_WhitespaceAnswer_ReturnsFailedWithFullConfidence()
    {
        // Act
        var result = await _service.EvaluateAnswerAsync(
            question: "What is your interest?",
            userAnswer: "   \t\n  ",
            evaluationCriteria: "Must show interest",
            groupTopic: "Tech Discussion");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Passed, Is.False);
        Assert.That(result.Reasoning, Is.EqualTo("No answer provided"));
        Assert.That(result.Confidence, Is.EqualTo(1.0));
    }

    [Test]
    public async Task EvaluateAnswerAsync_EmptyAnswer_DoesNotCallAI()
    {
        // Act
        await _service.EvaluateAnswerAsync(
            question: "Question",
            userAnswer: "",
            evaluationCriteria: "Criteria",
            groupTopic: "Topic");

        // Assert - should not call AI for empty answers
        await _mockChatService.DidNotReceive()
            .GetCompletionAsync(
                Arg.Any<AIFeatureType>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<ChatCompletionOptions>(),
                Arg.Any<CancellationToken>());
    }

    #endregion

    #region EvaluateAnswerAsync - Successful AI Response Tests

    [Test]
    public async Task EvaluateAnswerAsync_PassingAnswer_ReturnsPassedResult()
    {
        // Arrange
        var aiResponse = new ChatCompletionResult
        {
            Content = """
                {
                    "passed": true,
                    "reasoning": "The answer shows genuine interest in the topic.",
                    "confidence": 0.95
                }
                """
        };
        _mockChatService
            .GetCompletionAsync(
                Arg.Any<AIFeatureType>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<ChatCompletionOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(aiResponse);

        // Act
        var result = await _service.EvaluateAnswerAsync(
            question: "Why do you want to join?",
            userAnswer: "I'm passionate about technology and want to learn from experts.",
            evaluationCriteria: "Must demonstrate genuine interest",
            groupTopic: "Tech Discussion");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Passed, Is.True);
        Assert.That(result.Reasoning, Is.EqualTo("The answer shows genuine interest in the topic."));
        Assert.That(result.Confidence, Is.EqualTo(0.95));
    }

    [Test]
    public async Task EvaluateAnswerAsync_FailingAnswer_ReturnsFailedResult()
    {
        // Arrange
        var aiResponse = new ChatCompletionResult
        {
            Content = """
                {
                    "passed": false,
                    "reasoning": "Generic response that doesn't address the topic.",
                    "confidence": 0.85
                }
                """
        };
        _mockChatService
            .GetCompletionAsync(
                Arg.Any<AIFeatureType>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<ChatCompletionOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(aiResponse);

        // Act
        var result = await _service.EvaluateAnswerAsync(
            question: "Why do you want to join?",
            userAnswer: "Yes",
            evaluationCriteria: "Must demonstrate genuine interest",
            groupTopic: "Tech Discussion");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Passed, Is.False);
        Assert.That(result.Reasoning, Is.EqualTo("Generic response that doesn't address the topic."));
        Assert.That(result.Confidence, Is.EqualTo(0.85));
    }

    #endregion

    #region EvaluateAnswerAsync - Markdown Code Block Handling Tests

    [Test]
    public async Task EvaluateAnswerAsync_ResponseWithMarkdownCodeBlock_ParsesCorrectly()
    {
        // Arrange - AI sometimes wraps JSON in markdown code blocks
        var aiResponse = new ChatCompletionResult
        {
            Content = """
                ```json
                {
                    "passed": true,
                    "reasoning": "Good answer.",
                    "confidence": 0.9
                }
                ```
                """
        };
        _mockChatService
            .GetCompletionAsync(
                Arg.Any<AIFeatureType>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<ChatCompletionOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(aiResponse);

        // Act
        var result = await _service.EvaluateAnswerAsync(
            question: "Question",
            userAnswer: "My thoughtful answer",
            evaluationCriteria: "Criteria",
            groupTopic: "Topic");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Passed, Is.True);
        Assert.That(result.Reasoning, Is.EqualTo("Good answer."));
    }

    [Test]
    public async Task EvaluateAnswerAsync_ResponseWithPlainCodeBlock_ParsesCorrectly()
    {
        // Arrange - code block without language specifier
        var aiResponse = new ChatCompletionResult
        {
            Content = """
                ```
                {
                    "passed": false,
                    "reasoning": "Needs improvement.",
                    "confidence": 0.7
                }
                ```
                """
        };
        _mockChatService
            .GetCompletionAsync(
                Arg.Any<AIFeatureType>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<ChatCompletionOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(aiResponse);

        // Act
        var result = await _service.EvaluateAnswerAsync(
            question: "Question",
            userAnswer: "Answer",
            evaluationCriteria: "Criteria",
            groupTopic: "Topic");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Passed, Is.False);
        Assert.That(result.Confidence, Is.EqualTo(0.7));
    }

    #endregion

    #region EvaluateAnswerAsync - Null/Empty AI Response Tests

    [Test]
    public async Task EvaluateAnswerAsync_NullAIResponse_ReturnsNull()
    {
        // Arrange
        _mockChatService
            .GetCompletionAsync(
                Arg.Any<AIFeatureType>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<ChatCompletionOptions>(),
                Arg.Any<CancellationToken>())
            .Returns((ChatCompletionResult?)null);

        // Act
        var result = await _service.EvaluateAnswerAsync(
            question: "Question",
            userAnswer: "Answer",
            evaluationCriteria: "Criteria",
            groupTopic: "Topic");

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task EvaluateAnswerAsync_EmptyAIResponseContent_ReturnsNull()
    {
        // Arrange
        var aiResponse = new ChatCompletionResult { Content = "" };
        _mockChatService
            .GetCompletionAsync(
                Arg.Any<AIFeatureType>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<ChatCompletionOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(aiResponse);

        // Act
        var result = await _service.EvaluateAnswerAsync(
            question: "Question",
            userAnswer: "Answer",
            evaluationCriteria: "Criteria",
            groupTopic: "Topic");

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task EvaluateAnswerAsync_WhitespaceAIResponseContent_ReturnsNull()
    {
        // Arrange
        var aiResponse = new ChatCompletionResult { Content = "   \n\t  " };
        _mockChatService
            .GetCompletionAsync(
                Arg.Any<AIFeatureType>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<ChatCompletionOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(aiResponse);

        // Act
        var result = await _service.EvaluateAnswerAsync(
            question: "Question",
            userAnswer: "Answer",
            evaluationCriteria: "Criteria",
            groupTopic: "Topic");

        // Assert
        Assert.That(result, Is.Null);
    }

    #endregion

    #region EvaluateAnswerAsync - Confidence Clamping Tests

    [Test]
    public async Task EvaluateAnswerAsync_ConfidenceAboveOne_ClampedToOne()
    {
        // Arrange
        var aiResponse = new ChatCompletionResult
        {
            Content = """{"passed": true, "reasoning": "Great!", "confidence": 1.5}"""
        };
        _mockChatService
            .GetCompletionAsync(
                Arg.Any<AIFeatureType>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<ChatCompletionOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(aiResponse);

        // Act
        var result = await _service.EvaluateAnswerAsync(
            question: "Q", userAnswer: "A", evaluationCriteria: "C", groupTopic: "T");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Confidence, Is.EqualTo(1.0));
    }

    [Test]
    public async Task EvaluateAnswerAsync_ConfidenceBelowZero_ClampedToZero()
    {
        // Arrange
        var aiResponse = new ChatCompletionResult
        {
            Content = """{"passed": false, "reasoning": "Bad", "confidence": -0.5}"""
        };
        _mockChatService
            .GetCompletionAsync(
                Arg.Any<AIFeatureType>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<ChatCompletionOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(aiResponse);

        // Act
        var result = await _service.EvaluateAnswerAsync(
            question: "Q", userAnswer: "A", evaluationCriteria: "C", groupTopic: "T");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Confidence, Is.EqualTo(0.0));
    }

    #endregion

    #region EvaluateAnswerAsync - Missing/Null Reasoning Tests

    [Test]
    public async Task EvaluateAnswerAsync_NullReasoning_UsesDefaultMessage()
    {
        // Arrange
        var aiResponse = new ChatCompletionResult
        {
            Content = """{"passed": true, "reasoning": null, "confidence": 0.8}"""
        };
        _mockChatService
            .GetCompletionAsync(
                Arg.Any<AIFeatureType>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<ChatCompletionOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(aiResponse);

        // Act
        var result = await _service.EvaluateAnswerAsync(
            question: "Q", userAnswer: "A", evaluationCriteria: "C", groupTopic: "T");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Reasoning, Is.EqualTo("No reasoning provided"));
    }

    [Test]
    public async Task EvaluateAnswerAsync_MissingReasoningField_UsesDefaultMessage()
    {
        // Arrange - JSON without reasoning field
        var aiResponse = new ChatCompletionResult
        {
            Content = """{"passed": true, "confidence": 0.8}"""
        };
        _mockChatService
            .GetCompletionAsync(
                Arg.Any<AIFeatureType>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<ChatCompletionOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(aiResponse);

        // Act
        var result = await _service.EvaluateAnswerAsync(
            question: "Q", userAnswer: "A", evaluationCriteria: "C", groupTopic: "T");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Reasoning, Is.EqualTo("No reasoning provided"));
    }

    #endregion

    #region EvaluateAnswerAsync - Error Handling Tests

    [Test]
    public async Task EvaluateAnswerAsync_AIThrowsException_ReturnsNull()
    {
        // Arrange
        _mockChatService
            .GetCompletionAsync(
                Arg.Any<AIFeatureType>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<ChatCompletionOptions>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("AI service unavailable"));

        // Act
        var result = await _service.EvaluateAnswerAsync(
            question: "Question",
            userAnswer: "Answer",
            evaluationCriteria: "Criteria",
            groupTopic: "Topic");

        // Assert - should gracefully return null, not throw
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task EvaluateAnswerAsync_InvalidJson_ReturnsNull()
    {
        // Arrange
        var aiResponse = new ChatCompletionResult
        {
            Content = "This is not valid JSON at all"
        };
        _mockChatService
            .GetCompletionAsync(
                Arg.Any<AIFeatureType>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<ChatCompletionOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(aiResponse);

        // Act
        var result = await _service.EvaluateAnswerAsync(
            question: "Question",
            userAnswer: "Answer",
            evaluationCriteria: "Criteria",
            groupTopic: "Topic");

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task EvaluateAnswerAsync_MalformedJson_ReturnsNull()
    {
        // Arrange
        var aiResponse = new ChatCompletionResult
        {
            Content = """{"passed": true, "reasoning": "incomplete"""
        };
        _mockChatService
            .GetCompletionAsync(
                Arg.Any<AIFeatureType>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<ChatCompletionOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(aiResponse);

        // Act
        var result = await _service.EvaluateAnswerAsync(
            question: "Question",
            userAnswer: "Answer",
            evaluationCriteria: "Criteria",
            groupTopic: "Topic");

        // Assert
        Assert.That(result, Is.Null);
    }

    #endregion

    #region EvaluateAnswerAsync - Request Options Tests

    [Test]
    public async Task EvaluateAnswerAsync_UsesJsonModeAndFeatureConfigDefaults()
    {
        // Arrange
        ChatCompletionOptions? capturedOptions = null;
        _mockChatService
            .GetCompletionAsync(
                Arg.Any<AIFeatureType>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Do<ChatCompletionOptions>(o => capturedOptions = o),
                Arg.Any<CancellationToken>())
            .Returns(new ChatCompletionResult { Content = """{"passed": true, "reasoning": "OK", "confidence": 0.9}""" });

        // Act
        await _service.EvaluateAnswerAsync(
            question: "Q", userAnswer: "A", evaluationCriteria: "C", groupTopic: "T");

        // Assert - verify JSON mode for structured parsing; Temperature/MaxTokens come from feature config
        Assert.That(capturedOptions, Is.Not.Null);
        Assert.That(capturedOptions!.JsonMode, Is.True);
        Assert.That(capturedOptions.Temperature, Is.Null, "Temperature should come from feature config, not hardcoded");
        Assert.That(capturedOptions.MaxTokens, Is.Null, "MaxTokens should come from feature config, not hardcoded");

    }

    #endregion

    #region EvaluateAnswerAsync - Case Insensitive JSON Parsing Tests

    [Test]
    public async Task EvaluateAnswerAsync_UppercaseJsonKeys_ParsesCorrectly()
    {
        // Arrange - some AI models return uppercase keys
        var aiResponse = new ChatCompletionResult
        {
            Content = """{"Passed": true, "Reasoning": "Good job!", "Confidence": 0.88}"""
        };
        _mockChatService
            .GetCompletionAsync(
                Arg.Any<AIFeatureType>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<ChatCompletionOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(aiResponse);

        // Act
        var result = await _service.EvaluateAnswerAsync(
            question: "Q", userAnswer: "A", evaluationCriteria: "C", groupTopic: "T");

        // Assert - should parse despite case differences
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Passed, Is.True);
        Assert.That(result.Reasoning, Is.EqualTo("Good job!"));
        Assert.That(result.Confidence, Is.EqualTo(0.88));
    }

    #endregion
}
