using System.Text.Json;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.ContentDetection.Checks;
using TelegramGroupsAdmin.ContentDetection.Constants;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Services;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services.AI;

namespace TelegramGroupsAdmin.UnitTests.ContentDetection;

/// <summary>
/// Tests for AIContentCheckV2 with V2 scoring model (0.0-5.0 points)
/// Renamed from OpenAIContentCheckTests after Semantic Kernel refactoring.
/// </summary>
[TestFixture]
public class AIContentCheckTests
{
    private ILogger<AIContentCheckV2> _mockLogger = null!;
    private IChatService _mockChatService = null!;
    private IMessageContextProvider _mockMessageContextProvider = null!;
    private HybridCache _cache = null!;
    private ServiceProvider _serviceProvider = null!;
    private AIContentCheckV2 _check = null!;

    [SetUp]
    public void Setup()
    {
        _mockLogger = Substitute.For<ILogger<AIContentCheckV2>>();
        _mockChatService = Substitute.For<IChatService>();
        _mockMessageContextProvider = Substitute.For<IMessageContextProvider>();

        // Create HybridCache via DI
        var services = new ServiceCollection();
        services.AddHybridCache();
        _serviceProvider = services.BuildServiceProvider();
        _cache = _serviceProvider.GetRequiredService<HybridCache>();

        // Setup message context provider to return empty list by default
        _mockMessageContextProvider
            .GetRecentMessagesAsync(Arg.Any<long>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<HistoryMessage>>(new List<HistoryMessage>()));

        _check = new AIContentCheckV2(
            _mockLogger,
            _mockChatService,
            _cache,
            _mockMessageContextProvider
        );
    }

    [TearDown]
    public void TearDown()
    {
        _serviceProvider.Dispose();
    }

    #region ShouldExecute Tests

    [Test]
    public void ShouldExecute_EmptyMessage_ReturnsFalse()
    {
        // Arrange
        var request = new ContentCheckRequest
        {
            Message = "",
            User = UserIdentity.FromId(123),
            Chat = ChatIdentity.FromId(456),
            IsUserTrusted = false,
            IsUserAdmin = false
        };

        // Act
        var result = _check.ShouldExecute(request);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void ShouldExecute_WhitespaceMessage_ReturnsFalse()
    {
        // Arrange
        var request = new ContentCheckRequest
        {
            Message = "   \n\t  ",
            User = UserIdentity.FromId(123),
            Chat = ChatIdentity.FromId(456),
            IsUserTrusted = false,
            IsUserAdmin = false
        };

        // Act
        var result = _check.ShouldExecute(request);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void ShouldExecute_TrustedUser_ReturnsFalse()
    {
        // Arrange
        var request = new ContentCheckRequest
        {
            Message = "Test message",
            User = UserIdentity.FromId(123),
            Chat = ChatIdentity.FromId(456),
            IsUserTrusted = true,
            IsUserAdmin = false
        };

        // Act
        var result = _check.ShouldExecute(request);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void ShouldExecute_AdminUser_ReturnsFalse()
    {
        // Arrange
        var request = new ContentCheckRequest
        {
            Message = "Test message",
            User = UserIdentity.FromId(123),
            Chat = ChatIdentity.FromId(456),
            IsUserTrusted = false,
            IsUserAdmin = true
        };

        // Act
        var result = _check.ShouldExecute(request);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void ShouldExecute_ValidMessage_UntrustedUser_ReturnsTrue()
    {
        // Arrange
        var request = new ContentCheckRequest
        {
            Message = "Test message",
            User = UserIdentity.FromId(123),
            Chat = ChatIdentity.FromId(456),
            IsUserTrusted = false,
            IsUserAdmin = false
        };

        // Act
        var result = _check.ShouldExecute(request);

        // Assert
        Assert.That(result, Is.True);
    }

    #endregion

    #region CheckAsync - Short Message Tests

    [Test]
    public async Task CheckAsync_MessageTooShort_Abstains()
    {
        // Arrange
        var request = new AIVetoCheckRequest
        {
            Message = "Hi",
            User = UserIdentity.FromId(123),
            Chat = ChatIdentity.FromId(456),
            SystemPrompt = null,
            HasSpamFlags = false,
            MinMessageLength = 10,
            CheckShortMessages = false,
            MessageHistoryCount = 3,
            Model = "gpt-4",
            MaxTokens = 500,
            CancellationToken = CancellationToken.None
        };

        // Act
        var response = await _check.CheckAsync(request);

        // Assert
        Assert.That(response.Score, Is.EqualTo(0.0));
        Assert.That(response.Abstained, Is.True);
        Assert.That(response.Details, Does.Contain("too short"));
    }

    [Test]
    public async Task CheckAsync_MessageTooShort_ButCheckShortMessagesEnabled_CallsAPI()
    {
        // Arrange
        SetupChatService(CreateSpamResponse("Suspicious short message", 0.8));

        var request = new AIVetoCheckRequest
        {
            Message = "Hi",
            User = UserIdentity.FromId(123),
            Chat = ChatIdentity.FromId(456),
            SystemPrompt = null,
            HasSpamFlags = true,  // AI veto requires spam flags from other checks
            MinMessageLength = 10,
            CheckShortMessages = true,
            MessageHistoryCount = 3,
            Model = "gpt-4",
            MaxTokens = 500,
            CancellationToken = CancellationToken.None
        };

        // Act
        var response = await _check.CheckAsync(request);

        // Assert
        Assert.That(response.Score, Is.EqualTo(4.0).Within(0.01)); // 0.8 * 5.0
        Assert.That(response.Abstained, Is.False);
    }

    #endregion

    #region CheckAsync - Veto Mode Tests (AI always runs as veto)

    [Test]
    public async Task CheckAsync_NoSpamFlags_Abstains()
    {
        // AI veto only runs when other checks have flagged spam
        // Arrange
        var request = new AIVetoCheckRequest
        {
            Message = "This is a test message",
            User = UserIdentity.FromId(123),
            Chat = ChatIdentity.FromId(456),
            SystemPrompt = null,
            HasSpamFlags = false,
            MinMessageLength = 10,
            CheckShortMessages = false,
            MessageHistoryCount = 3,
            Model = "gpt-4",
            MaxTokens = 500,
            CancellationToken = CancellationToken.None
        };

        // Act
        var response = await _check.CheckAsync(request);

        // Assert
        Assert.That(response.Score, Is.EqualTo(0.0));
        Assert.That(response.Abstained, Is.True);
        Assert.That(response.Details, Does.Contain("No spam flags"));
    }

    [Test]
    public async Task CheckAsync_HasSpamFlags_CallsAPI()
    {
        // Arrange
        SetupChatService(CreateCleanResponse("Looks fine to me", 0.9));

        var request = new AIVetoCheckRequest
        {
            Message = "This is a test message",
            User = UserIdentity.FromId(123),
            Chat = ChatIdentity.FromId(456),
            SystemPrompt = null,
            HasSpamFlags = true,
            MinMessageLength = 10,
            CheckShortMessages = false,
            MessageHistoryCount = 3,
            Model = "gpt-4",
            MaxTokens = 500,
            CancellationToken = CancellationToken.None
        };

        // Act
        var response = await _check.CheckAsync(request);

        // Assert
        Assert.That(response.Score, Is.EqualTo(0.0));
        Assert.That(response.Abstained, Is.False); // Veto returns clean verdict, not abstention
        Assert.That(response.Details, Does.Contain("Clean"));
    }

    #endregion

    #region CheckAsync - AI Service Not Configured Tests

    [Test]
    public async Task CheckAsync_AIServiceNotConfigured_Abstains()
    {
        // Arrange - Chat service returns null (feature not configured)
        _mockChatService
            .GetCompletionAsync(
                Arg.Any<AIFeatureType>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<ChatCompletionOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ChatCompletionResult?>(null));

        var request = CreateValidRequest();

        // Act
        var response = await _check.CheckAsync(request);

        // Assert
        Assert.That(response.Score, Is.EqualTo(0.0));
        Assert.That(response.Abstained, Is.True);
    }

    #endregion

    #region CheckAsync - Spam Detection Tests

    [Test]
    public async Task CheckAsync_SpamDetected_HighConfidence_ReturnsHighScore()
    {
        // Arrange
        SetupChatService(CreateSpamResponse("This contains prohibited content", 0.95));

        var request = CreateValidRequest();

        // Act
        var response = await _check.CheckAsync(request);

        // Assert
        Assert.That(response.Score, Is.EqualTo(4.75).Within(0.01)); // 0.95 * 5.0
        Assert.That(response.Abstained, Is.False);
        Assert.That(response.CheckName, Is.EqualTo(CheckName.OpenAI));
        Assert.That(response.Details, Does.Contain("Spam"));
        Assert.That(response.Details, Does.Contain("prohibited content"));
    }

    [Test]
    public async Task CheckAsync_SpamDetected_MediumConfidence_ReturnsMediumScore()
    {
        // Arrange
        SetupChatService(CreateSpamResponse("Possibly spam", 0.6));

        var request = CreateValidRequest();

        // Act
        var response = await _check.CheckAsync(request);

        // Assert
        Assert.That(response.Score, Is.EqualTo(3.0).Within(0.01)); // 0.6 * 5.0
        Assert.That(response.Abstained, Is.False);
    }

    [Test]
    public async Task CheckAsync_SpamDetected_LowConfidence_ReturnsLowScore()
    {
        // Arrange
        SetupChatService(CreateSpamResponse("Slightly suspicious", 0.3));

        var request = CreateValidRequest();

        // Act
        var response = await _check.CheckAsync(request);

        // Assert
        Assert.That(response.Score, Is.EqualTo(1.5).Within(0.01)); // 0.3 * 5.0
        Assert.That(response.Abstained, Is.False);
    }

    #endregion

    #region CheckAsync - Review Detection Tests

    [Test]
    public async Task CheckAsync_ReviewResult_HighConfidence_CappedAt3Points()
    {
        // Arrange
        SetupChatService(CreateReviewResponse("Needs human review", 0.9));

        var request = CreateValidRequest();

        // Act
        var response = await _check.CheckAsync(request);

        // Assert
        // Review is capped at ContentDetectionConstants.ReviewThreshold even though 0.9 * 5.0 = 4.5
        Assert.That(response.Score, Is.EqualTo(ContentDetectionConstants.ReviewThreshold).Within(0.01));
        Assert.That(response.Abstained, Is.False);
        Assert.That(response.Details, Does.Contain("Review"));
    }

    [Test]
    public async Task CheckAsync_ReviewResult_MediumConfidence_ReturnsScore()
    {
        // Arrange
        SetupChatService(CreateReviewResponse("Uncertain", 0.5));

        var request = CreateValidRequest();

        // Act
        var response = await _check.CheckAsync(request);

        // Assert
        Assert.That(response.Score, Is.EqualTo(2.5).Within(0.01)); // 0.5 * 5.0
        Assert.That(response.Abstained, Is.False);
    }

    #endregion

    #region CheckAsync - Clean Detection Tests

    [Test]
    public async Task CheckAsync_CleanResult_ReturnsCleanVerdict()
    {
        // Arrange
        SetupChatService(CreateCleanResponse("This is a legitimate message", 0.85));

        var request = CreateValidRequest();

        // Act
        var response = await _check.CheckAsync(request);

        // Assert
        Assert.That(response.Score, Is.EqualTo(0.0));
        Assert.That(response.Abstained, Is.False); // Clean is a verdict, not an abstention
        Assert.That(response.Details, Does.Contain("Clean"));
        Assert.That(response.Details, Does.Contain("legitimate message"));
    }

    #endregion

    #region CheckAsync - Caching Tests

    [Test]
    public async Task CheckAsync_SecondCall_UsesCachedResult()
    {
        // Arrange
        SetupChatService(CreateSpamResponse("Spam detected", 0.8));

        var request = CreateValidRequest();

        // Act - First call
        var response1 = await _check.CheckAsync(request);
        // Act - Second call with same message
        var response2 = await _check.CheckAsync(request);

        // Assert
        Assert.That(response1.Score, Is.EqualTo(response2.Score));
        Assert.That(response2.Details, Does.Contain("cached"));

        // Verify chat service was only called once (cached on second call)
        await _mockChatService.Received(1).GetCompletionAsync(
            Arg.Any<AIFeatureType>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<ChatCompletionOptions?>(),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region CheckAsync - Error Handling Tests

    [Test]
    public async Task CheckAsync_APIReturnsNull_Abstains()
    {
        // Arrange - Chat service returns null (API error)
        _mockChatService
            .GetCompletionAsync(Arg.Any<AIFeatureType>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ChatCompletionOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ChatCompletionResult?>(null));

        var request = CreateValidRequest();

        // Act
        var response = await _check.CheckAsync(request);

        // Assert
        Assert.That(response.Score, Is.EqualTo(0.0));
        Assert.That(response.Abstained, Is.True);
    }

    [Test]
    public async Task CheckAsync_Exception_Abstains()
    {
        // Arrange
        _mockChatService
            .GetCompletionAsync(Arg.Any<AIFeatureType>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ChatCompletionOptions?>(), Arg.Any<CancellationToken>())
            .Returns<ChatCompletionResult?>(_ => throw new InvalidOperationException("Network error"));

        var request = CreateValidRequest();

        // Act
        var response = await _check.CheckAsync(request);

        // Assert
        Assert.That(response.Score, Is.EqualTo(0.0));
        Assert.That(response.Abstained, Is.True);
        Assert.That(response.Error, Is.Not.Null);
    }

    [Test]
    public async Task CheckAsync_InvalidJsonResponse_Abstains()
    {
        // Arrange
        var invalidResult = new ChatCompletionResult { Content = "This is not valid JSON", TotalTokens = 10 };
        _mockChatService
            .GetCompletionAsync(Arg.Any<AIFeatureType>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ChatCompletionOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ChatCompletionResult?>(invalidResult));

        var request = CreateValidRequest();

        // Act
        var response = await _check.CheckAsync(request);

        // Assert
        Assert.That(response.Score, Is.EqualTo(0.0));
        Assert.That(response.Abstained, Is.True);
        Assert.That(response.Details, Does.Contain("Failed to parse"));
    }

    [Test]
    public async Task CheckAsync_EmptyContent_Abstains()
    {
        // Arrange
        var emptyResult = new ChatCompletionResult { Content = "", TotalTokens = 0 };
        _mockChatService
            .GetCompletionAsync(Arg.Any<AIFeatureType>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ChatCompletionOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ChatCompletionResult?>(emptyResult));

        var request = CreateValidRequest();

        // Act
        var response = await _check.CheckAsync(request);

        // Assert
        Assert.That(response.Score, Is.EqualTo(0.0));
        Assert.That(response.Abstained, Is.True);
    }

    #endregion

    #region CheckAsync - Edge Cases

    [Test]
    public async Task CheckAsync_MissingConfidence_UsesDefault()
    {
        // Arrange - JSON response with missing confidence field
        var jsonResponse = JsonSerializer.Serialize(new
        {
            result = "spam",
            reason = "Test spam"
            // No confidence field
        });
        var result = new ChatCompletionResult { Content = jsonResponse, TotalTokens = 20 };
        _mockChatService
            .GetCompletionAsync(Arg.Any<AIFeatureType>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ChatCompletionOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ChatCompletionResult?>(result));

        var request = CreateValidRequest();

        // Act
        var response = await _check.CheckAsync(request);

        // Assert
        // Default confidence is 0.8, so score should be 0.8 * 5.0 = 4.0
        Assert.That(response.Score, Is.EqualTo(4.0).Within(0.01));
        Assert.That(response.Abstained, Is.False);
    }

    [Test]
    public async Task CheckAsync_UnknownResult_Abstains()
    {
        // Arrange - JSON response with unknown result value
        var jsonResponse = JsonSerializer.Serialize(new
        {
            result = "unknown_value",
            reason = "Test",
            confidence = 0.5
        });
        var result = new ChatCompletionResult { Content = jsonResponse, TotalTokens = 20 };
        _mockChatService
            .GetCompletionAsync(Arg.Any<AIFeatureType>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ChatCompletionOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ChatCompletionResult?>(result));

        var request = CreateValidRequest();

        // Act
        var response = await _check.CheckAsync(request);

        // Assert
        Assert.That(response.Score, Is.EqualTo(0.0));
        Assert.That(response.Abstained, Is.True); // Unknown result should abstain
    }

    #endregion

    #region CheckAsync - OCR Passthrough Tests

    [Test]
    public async Task CheckAsync_OcrOnlyMessage_AnalyzesOcrText()
    {
        // Arrange - Image with no caption but OCR extracted text
        SetupChatService(CreateSpamResponse("Spam in image text", 0.9));

        var request = CreateRequestWithOcr(
            message: "",
            ocrText: "BUY CRYPTO NOW! 100x GUARANTEED RETURNS!");

        // Act
        var response = await _check.CheckAsync(request);

        // Assert
        Assert.That(response.Score, Is.EqualTo(4.5).Within(0.01)); // 0.9 * 5.0
        Assert.That(response.Abstained, Is.False);

        // Verify AI service was called (OCR text should be analyzed)
        await _mockChatService.Received(1).GetCompletionAsync(
            Arg.Any<AIFeatureType>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<ChatCompletionOptions?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CheckAsync_CaptionPlusOcr_CombinesTextWithSeparator()
    {
        // Arrange - Message with both caption and OCR text
        SetupChatService(CreateCleanResponse("Legitimate content", 0.85));

        var request = CreateRequestWithOcr(
            message: "Check out this screenshot",
            ocrText: "Meeting notes from today");

        // Act
        var response = await _check.CheckAsync(request);

        // Assert - Clean verdict
        Assert.That(response.Score, Is.EqualTo(0.0));
        Assert.That(response.Abstained, Is.False);
    }

    [Test]
    public async Task CheckAsync_ShortCaptionWithLongOcr_PassesMinLengthCheck()
    {
        // Arrange - Short caption (< 10 chars) but long OCR text
        SetupChatService(CreateSpamResponse("Spam detected", 0.75));

        var request = CreateRequestWithOcr(
            message: "Hi",  // Only 2 chars - would normally be too short
            ocrText: "This is a much longer text extracted from the image via OCR");

        // Act
        var response = await _check.CheckAsync(request);

        // Assert - Should NOT abstain because combined length > 10
        Assert.That(response.Abstained, Is.False);
        Assert.That(response.Score, Is.EqualTo(3.75).Within(0.01)); // 0.75 * 5.0
    }

    [Test]
    public async Task CheckAsync_ShortOcrOnly_Abstains()
    {
        // Arrange - Only short OCR text, no caption
        var request = CreateRequestWithOcr(
            message: "",
            ocrText: "Hi");  // Only 2 chars - too short

        // Act
        var response = await _check.CheckAsync(request);

        // Assert - Should abstain because combined text too short
        Assert.That(response.Abstained, Is.True);
        Assert.That(response.Details, Does.Contain("too short"));
    }

    [Test]
    public async Task CheckAsync_DifferentOcrSameCaption_SeparateCacheEntries()
    {
        // Arrange - Same caption but different OCR text should use different cache keys
        SetupChatService(CreateSpamResponse("Spam detected", 0.8));

        var request1 = CreateRequestWithOcr(
            message: "Check this out",
            ocrText: "First image OCR text with spam content");

        var request2 = CreateRequestWithOcr(
            message: "Check this out",  // Same caption
            ocrText: "Different image OCR text also spam");  // Different OCR

        // Act
        await _check.CheckAsync(request1);
        await _check.CheckAsync(request2);

        // Assert - Chat service should be called twice (different cache keys)
        await _mockChatService.Received(2).GetCompletionAsync(
            Arg.Any<AIFeatureType>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<ChatCompletionOptions?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CheckAsync_SameOcrSameCaption_UsesCachedResult()
    {
        // Arrange - Identical requests should use cache
        SetupChatService(CreateSpamResponse("Spam detected", 0.8));

        var request1 = CreateRequestWithOcr(
            message: "Check this out",
            ocrText: "Same OCR text");

        var request2 = CreateRequestWithOcr(
            message: "Check this out",
            ocrText: "Same OCR text");  // Identical

        // Act
        var response1 = await _check.CheckAsync(request1);
        var response2 = await _check.CheckAsync(request2);

        // Assert - Chat service should only be called once (cached on second call)
        await _mockChatService.Received(1).GetCompletionAsync(
            Arg.Any<AIFeatureType>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<ChatCompletionOptions?>(),
            Arg.Any<CancellationToken>());

        Assert.That(response2.Details, Does.Contain("cached"));
    }

    #endregion

    #region Helper Methods

    private AIVetoCheckRequest CreateRequestWithOcr(string message, string ocrText)
    {
        return new AIVetoCheckRequest
        {
            Message = message,
            User = UserIdentity.FromId(123),
            Chat = ChatIdentity.FromId(456),
            SystemPrompt = null,
            HasSpamFlags = true,
            MinMessageLength = 10,
            CheckShortMessages = false,
            MessageHistoryCount = 3,
            Model = "gpt-4",
            MaxTokens = 500,
            OcrExtractedText = ocrText,
            CancellationToken = CancellationToken.None
        };
    }

    private AIVetoCheckRequest CreateValidRequest()
    {
        // AI always runs as veto - HasSpamFlags = true means other checks flagged as spam
        return new AIVetoCheckRequest
        {
            Message = "This is a test message that is long enough to be checked",
            User = UserIdentity.FromId(123),
            Chat = ChatIdentity.FromId(456),
            SystemPrompt = null,
            HasSpamFlags = true,  // AI veto requires spam flags from other checks
            MinMessageLength = 10,
            CheckShortMessages = false,
            MessageHistoryCount = 3,
            Model = "gpt-4",
            MaxTokens = 500,
            CancellationToken = CancellationToken.None
        };
    }

    private static ChatCompletionResult CreateSpamResponse(string reason, double confidence)
    {
        var jsonResponse = JsonSerializer.Serialize(new
        {
            result = "spam",
            reason,
            confidence
        });
        return new ChatCompletionResult { Content = jsonResponse, TotalTokens = 50 };
    }

    private static ChatCompletionResult CreateCleanResponse(string reason, double confidence)
    {
        var jsonResponse = JsonSerializer.Serialize(new
        {
            result = "clean",
            reason,
            confidence
        });
        return new ChatCompletionResult { Content = jsonResponse, TotalTokens = 50 };
    }

    private static ChatCompletionResult CreateReviewResponse(string reason, double confidence)
    {
        var jsonResponse = JsonSerializer.Serialize(new
        {
            result = "review",
            reason,
            confidence
        });
        return new ChatCompletionResult { Content = jsonResponse, TotalTokens = 50 };
    }

    private void SetupChatService(ChatCompletionResult response)
    {
        _mockChatService
            .GetCompletionAsync(Arg.Any<AIFeatureType>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ChatCompletionOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ChatCompletionResult?>(response));
    }

    #endregion
}
