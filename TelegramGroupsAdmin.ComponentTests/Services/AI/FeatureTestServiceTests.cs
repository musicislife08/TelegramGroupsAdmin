using Microsoft.Extensions.Logging;
using NSubstitute;
using NUnit.Framework;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Core.Services.AI;

namespace TelegramGroupsAdmin.ComponentTests.Services.AI;

/// <summary>
/// Unit tests for FeatureTestService
/// Focus: Test validation logic and error handling
/// Note: Full flow tests with actual HTTP calls should be in integration tests with WireMock
/// </summary>
[TestFixture]
public class FeatureTestServiceTests
{
    private IChatService _mockChatService = null!;
    private ILogger<FeatureTestService> _mockLogger = null!;
    private FeatureTestService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _mockChatService = Substitute.For<IChatService>();
        _mockLogger = Substitute.For<ILogger<FeatureTestService>>();

        _service = new FeatureTestService(
            _mockChatService,
            _mockLogger);
    }

    #region Validation Tests

    [Test]
    public void TestFeatureAsync_NullConnectionId_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await _service.TestFeatureAsync(
                AIFeatureType.SpamDetection,
                null!,
                "gpt-4o-mini"));
    }

    [Test]
    public void TestFeatureAsync_NullModel_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await _service.TestFeatureAsync(
                AIFeatureType.SpamDetection,
                "test-connection",
                null!));
    }

    [Test]
    public async Task TestFeatureAsync_ChatServiceReturnsNull_ReturnsFailure()
    {
        // Arrange - Chat service returns null (connection not found or disabled)
        _mockChatService
            .TestCompletionAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<ChatCompletionOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ChatCompletionResult?>(null));

        // Act
        var result = await _service.TestFeatureAsync(
            AIFeatureType.SpamDetection,
            "test-connection",
            "gpt-4o-mini");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Does.Contain("No response received"));
        });
    }

    [Test]
    public async Task TestFeatureAsync_ChatServiceReturnsEmptyContent_ReturnsFailure()
    {
        // Arrange - Chat service returns result with empty content
        _mockChatService
            .TestCompletionAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<ChatCompletionOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ChatCompletionResult?>(new ChatCompletionResult { Content = "" }));

        // Act
        var result = await _service.TestFeatureAsync(
            AIFeatureType.SpamDetection,
            "test-connection",
            "gpt-4o-mini");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Does.Contain("No response received"));
        });
    }

    [Test]
    public async Task TestFeatureAsync_SpamDetection_Success()
    {
        // Arrange
        _mockChatService
            .TestCompletionAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<ChatCompletionOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ChatCompletionResult?>(new ChatCompletionResult
            {
                Content = "OK",
                TotalTokens = 5
            }));

        // Act
        var result = await _service.TestFeatureAsync(
            AIFeatureType.SpamDetection,
            "test-connection",
            "gpt-4o-mini");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.Message, Does.Contain("Model responded successfully"));
            Assert.That(result.Message, Does.Contain("5 tokens"));
        });
    }

    [Test]
    public async Task TestFeatureAsync_Translation_Success()
    {
        // Arrange
        _mockChatService
            .TestCompletionAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<ChatCompletionOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ChatCompletionResult?>(new ChatCompletionResult
            {
                Content = "Hola",
                TotalTokens = 3
            }));

        // Act
        var result = await _service.TestFeatureAsync(
            AIFeatureType.Translation,
            "test-connection",
            "gpt-4o-mini");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.Message, Does.Contain("Translation test passed"));
        });
    }

    [Test]
    public async Task TestFeatureAsync_ImageAnalysis_VisionSuccess()
    {
        // Arrange
        _mockChatService
            .TestVisionCompletionAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<byte[]>(),
                Arg.Any<string>(),
                Arg.Any<ChatCompletionOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ChatCompletionResult?>(new ChatCompletionResult
            {
                Content = "red",
                TotalTokens = 8
            }));

        // Act
        var result = await _service.TestFeatureAsync(
            AIFeatureType.ImageAnalysis,
            "test-connection",
            "gpt-4o-mini");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.Message, Does.Contain("Vision test passed"));
            Assert.That(result.Message, Does.Contain("image"));
        });
    }

    [Test]
    public async Task TestFeatureAsync_VideoAnalysis_VisionSuccess()
    {
        // Arrange
        _mockChatService
            .TestVisionCompletionAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<byte[]>(),
                Arg.Any<string>(),
                Arg.Any<ChatCompletionOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ChatCompletionResult?>(new ChatCompletionResult
            {
                Content = "red",
                TotalTokens = 10
            }));

        // Act
        var result = await _service.TestFeatureAsync(
            AIFeatureType.VideoAnalysis,
            "test-connection",
            "gpt-4o-mini");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.Message, Does.Contain("Vision test passed"));
            Assert.That(result.Message, Does.Contain("video frame"));
        });
    }

    [Test]
    public async Task TestFeatureAsync_VisionReturnsNull_ReturnsVisionNotSupported()
    {
        // Arrange - Vision call returns null (model doesn't support vision)
        _mockChatService
            .TestVisionCompletionAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<byte[]>(),
                Arg.Any<string>(),
                Arg.Any<ChatCompletionOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ChatCompletionResult?>(null));

        // Act
        var result = await _service.TestFeatureAsync(
            AIFeatureType.ImageAnalysis,
            "test-connection",
            "gpt-4o-mini");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Does.Contain("vision"));
        });
    }

    [Test]
    public async Task TestFeatureAsync_PromptBuilder_Success()
    {
        // Arrange
        _mockChatService
            .TestCompletionAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<ChatCompletionOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ChatCompletionResult?>(new ChatCompletionResult
            {
                Content = "ready",
                TotalTokens = 4
            }));

        // Act
        var result = await _service.TestFeatureAsync(
            AIFeatureType.PromptBuilder,
            "test-connection",
            "gpt-4o-mini");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.Message, Does.Contain("Prompt builder test passed"));
        });
    }

    [Test]
    public async Task TestFeatureAsync_UnknownFeatureType_ReturnsFailure()
    {
        // Act - Use invalid enum value
        var invalidFeatureType = (AIFeatureType)999;
        var result = await _service.TestFeatureAsync(
            invalidFeatureType,
            "test-connection",
            "gpt-4o-mini");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Does.Contain("Unknown feature type"));
        });
    }

    [Test]
    public async Task TestFeatureAsync_ExceptionThrown_ReturnsFailure()
    {
        // Arrange - Chat service throws exception
        _mockChatService
            .TestCompletionAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<ChatCompletionOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns<ChatCompletionResult?>(_ => throw new InvalidOperationException("Connection failed"));

        // Act
        var result = await _service.TestFeatureAsync(
            AIFeatureType.SpamDetection,
            "test-connection",
            "gpt-4o-mini");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Does.Contain("Test failed unexpectedly"));
            Assert.That(result.ErrorDetails, Does.Contain("Connection failed"));
        });
    }

    #endregion
}
