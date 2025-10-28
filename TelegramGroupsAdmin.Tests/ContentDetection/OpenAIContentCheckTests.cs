using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TelegramGroupsAdmin.ContentDetection.Checks;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Services;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace TelegramGroupsAdmin.Tests.ContentDetection;

/// <summary>
/// Comprehensive tests for OpenAIContentCheck using WireMock.Net to mock OpenAI API.
/// Tests prompt generation, JSON/legacy response parsing, error handling, caching, and confidence calculation.
/// </summary>
[TestFixture]
public class OpenAIContentCheckTests
{
    private WireMockServer _mockServer = null!;
    private IHttpClientFactory _httpClientFactory = null!;
    private ILogger<OpenAIContentCheck> _logger = null!;
    private IMemoryCache _cache = null!;
    private IMessageHistoryService _messageHistoryService = null!;
    private OpenAIContentCheck _sut = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        // Start WireMock server once for all tests
        _mockServer = WireMockServer.Start();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _mockServer?.Stop();
        _mockServer?.Dispose();
    }

    [SetUp]
    public void SetUp()
    {
        // Reset WireMock server for each test
        _mockServer.Reset();

        // Create real HttpClient pointing to WireMock server
        var httpClient = new HttpClient
        {
            BaseAddress = new Uri(_mockServer.Url!)
        };

        _httpClientFactory = Substitute.For<IHttpClientFactory>();
        _httpClientFactory.CreateClient("OpenAI").Returns(httpClient);

        // Create real MemoryCache for caching tests
        _cache = new MemoryCache(new MemoryCacheOptions());

        // Mock logger
        _logger = Substitute.For<ILogger<OpenAIContentCheck>>();

        // Mock message history service
        _messageHistoryService = Substitute.For<IMessageHistoryService>();
        _messageHistoryService
            .GetRecentMessagesAsync(Arg.Any<long>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Enumerable.Empty<HistoryMessage>()));

        // Create system under test
        _sut = new OpenAIContentCheck(_logger, _httpClientFactory, _cache, _messageHistoryService);
    }

    [TearDown]
    public void TearDown()
    {
        _cache?.Dispose();
    }

    #region ShouldExecute Tests

    [Test]
    public void ShouldExecute_WithValidMessage_ReturnsTrue()
    {
        // Arrange
        var request = CreateCheckRequest("This is a test message");

        // Act
        var result = _sut.ShouldExecute(request);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void ShouldExecute_WithEmptyMessage_ReturnsFalse()
    {
        // Arrange
        var request = CreateCheckRequest("");

        // Act
        var result = _sut.ShouldExecute(request);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void ShouldExecute_WithWhitespaceMessage_ReturnsFalse()
    {
        // Arrange
        var request = CreateCheckRequest("   \t\n  ");

        // Act
        var result = _sut.ShouldExecute(request);

        // Assert
        Assert.That(result, Is.False);
    }

    #endregion

    #region JSON Response Parsing Tests

    [Test]
    public async Task CheckAsync_WithValidJsonSpamResponse_ReturnsSpamResult()
    {
        // Arrange
        var request = CreateOpenAICheckRequest("Buy crypto now! This is a longer message to meet minimum length", vetoMode: false, minMessageLength: 0);
        MockSuccessfulJsonResponse("spam", "Contains promotional content", 0.95);

        // Act
        var response = await _sut.CheckAsync(request);

        // Assert
        Assert.That(response.CheckName, Is.EqualTo("OpenAI"));
        Assert.That(response.Result, Is.EqualTo(CheckResultType.Spam));
        Assert.That(response.Confidence, Is.EqualTo(95));
        Assert.That(response.Details, Does.Contain("OpenAI detected spam"));
        Assert.That(response.Details, Does.Contain("Contains promotional content"));
        Assert.That(response.Error, Is.Null);
    }

    [Test]
    public async Task CheckAsync_WithValidJsonCleanResponse_ReturnsCleanResult()
    {
        // Arrange
        var request = CreateOpenAICheckRequest("Hello everyone, how are you today?", vetoMode: false, minMessageLength: 0);
        MockSuccessfulJsonResponse("clean", "Normal conversation", 0.85);

        // Act
        var response = await _sut.CheckAsync(request);

        // Assert
        Assert.That(response.CheckName, Is.EqualTo("OpenAI"));
        Assert.That(response.Result, Is.EqualTo(CheckResultType.Clean));
        Assert.That(response.Confidence, Is.EqualTo(85));
        Assert.That(response.Details, Does.Contain("OpenAI found no spam"));
        Assert.That(response.Details, Does.Contain("Normal conversation"));
        Assert.That(response.Error, Is.Null);
    }

    [Test]
    public async Task CheckAsync_WithValidJsonReviewResponse_ReturnsReviewResult()
    {
        // Arrange
        var request = CreateOpenAICheckRequest("Check out this link for more details", vetoMode: false, minMessageLength: 0);
        MockSuccessfulJsonResponse("review", "Uncertain - needs human review", 0.5);

        // Act
        var response = await _sut.CheckAsync(request);

        // Assert
        Assert.That(response.CheckName, Is.EqualTo("OpenAI"));
        Assert.That(response.Result, Is.EqualTo(CheckResultType.Review));
        Assert.That(response.Confidence, Is.EqualTo(50));
        Assert.That(response.Details, Does.Contain("OpenAI flagged for review"));
        Assert.That(response.Details, Does.Contain("Uncertain - needs human review"));
        Assert.That(response.Error, Is.Null);
    }

    [Test]
    public async Task CheckAsync_WithJsonResponse_UppercaseResult_ParsesCorrectly()
    {
        // Arrange
        var request = CreateOpenAICheckRequest("Test message for uppercase parsing", vetoMode: false, minMessageLength: 0);
        MockSuccessfulJsonResponse("SPAM", "Test uppercase", 0.9);

        // Act
        var response = await _sut.CheckAsync(request);

        // Assert
        Assert.That(response.Result, Is.EqualTo(CheckResultType.Spam));
        Assert.That(response.Confidence, Is.EqualTo(90));
    }

    [Test]
    public async Task CheckAsync_WithJsonResponse_MixedCaseResult_ParsesCorrectly()
    {
        // Arrange
        var request = CreateOpenAICheckRequest("Test message for mixed case parsing", vetoMode: false, minMessageLength: 0);
        MockSuccessfulJsonResponse("ClEaN", "Test mixed case", 0.8);

        // Act
        var response = await _sut.CheckAsync(request);

        // Assert
        Assert.That(response.Result, Is.EqualTo(CheckResultType.Clean));
    }

    [Test]
    public async Task CheckAsync_WithJsonResponse_UnknownResult_DefaultsToClean()
    {
        // Arrange
        var request = CreateOpenAICheckRequest("Test message for unknown result parsing", vetoMode: false, minMessageLength: 0);
        MockSuccessfulJsonResponse("unknown", "Unknown result type", 0.5);

        // Act
        var response = await _sut.CheckAsync(request);

        // Assert
        Assert.That(response.Result, Is.EqualTo(CheckResultType.Clean), "Unknown result types should fail open to Clean");
        Assert.That(response.Confidence, Is.EqualTo(50));
    }

    [Test]
    public async Task CheckAsync_WithJsonResponse_NullConfidence_DefaultsTo80Percent()
    {
        // Arrange
        var request = CreateOpenAICheckRequest("Test message for null confidence handling", vetoMode: false, minMessageLength: 0);
        var jsonResponse = @"{
            ""result"": ""spam"",
            ""reason"": ""Test without confidence"",
            ""confidence"": null
        }";
        MockOpenAISuccessResponse(jsonResponse);

        // Act
        var response = await _sut.CheckAsync(request);

        // Assert
        Assert.That(response.Confidence, Is.EqualTo(80), "Null confidence should default to 80%");
    }

    #endregion

    #region Veto Mode Tests

    [Test]
    public async Task CheckAsync_VetoMode_WithNoSpamFlags_SkipsOpenAICall()
    {
        // Arrange
        var request = CreateOpenAICheckRequest(
            "Test message",
            vetoMode: true,
            hasSpamFlags: false);

        // Act
        var response = await _sut.CheckAsync(request);

        // Assert
        Assert.That(response.Result, Is.EqualTo(CheckResultType.Clean));
        Assert.That(response.Details, Does.Contain("Veto mode: no spam flags from other checks"));
        Assert.That(response.Confidence, Is.EqualTo(0));

        // Verify no API call was made
        Assert.That(_mockServer.LogEntries.Count(), Is.EqualTo(0));
    }

    [Test]
    public async Task CheckAsync_VetoMode_WithSpamFlags_CallsOpenAI()
    {
        // Arrange
        var request = CreateOpenAICheckRequest(
            "Test message",
            vetoMode: true,
            hasSpamFlags: true);
        MockSuccessfulJsonResponse("clean", "False positive - legitimate message", 0.9);

        // Act
        var response = await _sut.CheckAsync(request);

        // Assert
        Assert.That(response.Result, Is.EqualTo(CheckResultType.Clean));
        Assert.That(response.Details, Does.Contain("OpenAI vetoed spam"));
        Assert.That(response.Details, Does.Contain("False positive - legitimate message"));
        Assert.That(response.Confidence, Is.EqualTo(90));

        // Verify API call was made
        Assert.That(_mockServer.LogEntries.Count(), Is.EqualTo(1));
    }

    [Test]
    public async Task CheckAsync_VetoMode_ConfirmsSpam_ReturnsSpamResult()
    {
        // Arrange
        var request = CreateOpenAICheckRequest(
            "Buy crypto now!",
            vetoMode: true,
            hasSpamFlags: true);
        MockSuccessfulJsonResponse("spam", "Confirmed spam - promotional content", 0.95);

        // Act
        var response = await _sut.CheckAsync(request);

        // Assert
        Assert.That(response.Result, Is.EqualTo(CheckResultType.Spam));
        Assert.That(response.Details, Does.Contain("OpenAI confirmed spam"));
        Assert.That(response.Details, Does.Contain("Confirmed spam - promotional content"));
        Assert.That(response.Confidence, Is.EqualTo(95));
    }

    #endregion

    #region Error Handling Tests

    [Test]
    public async Task CheckAsync_WithMalformedJson_FailsOpen()
    {
        // Arrange
        var request = CreateOpenAICheckRequest("Test message for malformed JSON handling", vetoMode: false, minMessageLength: 0);
        var malformedJson = @"{""result"": ""spam"", ""reason"": ""Missing closing brace""";
        MockOpenAISuccessResponse(malformedJson);

        // Act
        var response = await _sut.CheckAsync(request);

        // Assert
        Assert.That(response.Result, Is.EqualTo(CheckResultType.Clean), "Should fail open on malformed JSON");
        Assert.That(response.Confidence, Is.EqualTo(0));
        Assert.That(response.Details, Does.Contain("OpenAI error"));
        Assert.That(response.Details, Does.Contain("JSON parsing error"));
    }

    [Test]
    public async Task CheckAsync_WithEmptyJsonResponse_ReturnsCleanWithFallback()
    {
        // Arrange
        var request = CreateOpenAICheckRequest("Test message for empty JSON response", vetoMode: false, minMessageLength: 0);
        MockOpenAISuccessResponse("{}");

        // Act
        var response = await _sut.CheckAsync(request);

        // Assert
        Assert.That(response.Result, Is.EqualTo(CheckResultType.Clean));
        Assert.That(response.Confidence, Is.EqualTo(80), "Null confidence defaults to 80%");
    }

    [Test]
    public async Task CheckAsync_WithHttp429RateLimit_ReturnsCleanAndFailsOpen()
    {
        // Arrange
        var request = CreateOpenAICheckRequest("Test message for rate limit handling", vetoMode: false, minMessageLength: 0);
        _mockServer
            .Given(Request.Create().WithPath("/chat/completions").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(HttpStatusCode.TooManyRequests)
                .WithBody(@"{""error"": {""message"": ""Rate limit exceeded""}}"));

        // Act
        var response = await _sut.CheckAsync(request);

        // Assert
        Assert.That(response.Result, Is.EqualTo(CheckResultType.Clean), "Should fail open during rate limiting");
        Assert.That(response.Details, Does.Contain("OpenAI API rate limited - allowing message"));
        Assert.That(response.Confidence, Is.EqualTo(0));
        Assert.That(response.Error, Is.Not.Null);
        Assert.That(response.Error, Is.TypeOf<HttpRequestException>());
    }

    [Test]
    public async Task CheckAsync_WithHttp500ServerError_ReturnsCleanAndFailsOpen()
    {
        // Arrange
        var request = CreateOpenAICheckRequest("Test message for server error handling", vetoMode: false, minMessageLength: 0);
        _mockServer
            .Given(Request.Create().WithPath("/chat/completions").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(HttpStatusCode.InternalServerError)
                .WithBody(@"{""error"": {""message"": ""Internal server error""}}"));

        // Act
        var response = await _sut.CheckAsync(request);

        // Assert
        Assert.That(response.Result, Is.EqualTo(CheckResultType.Clean), "Should fail open on server errors");
        Assert.That(response.Details, Does.Contain("OpenAI API error"));
        Assert.That(response.Details, Does.Contain("InternalServerError"));
        Assert.That(response.Confidence, Is.EqualTo(0));
        Assert.That(response.Error, Is.Not.Null);
    }

    [Test]
    public async Task CheckAsync_WithEmptyApiResponse_ReturnsClean()
    {
        // Arrange
        var request = CreateOpenAICheckRequest("Test message for empty API response", vetoMode: false, minMessageLength: 0);
        var emptyResponse = @"{""choices"": []}";
        MockOpenAISuccessResponse(emptyResponse);

        // Act
        var response = await _sut.CheckAsync(request);

        // Assert
        Assert.That(response.Result, Is.EqualTo(CheckResultType.Clean));
        Assert.That(response.Details, Does.Contain("Invalid OpenAI response"));
        Assert.That(response.Error, Is.TypeOf<InvalidOperationException>());
    }

    [Test]
    public async Task CheckAsync_WithNullChoices_ReturnsClean()
    {
        // Arrange
        var request = CreateOpenAICheckRequest("Test message for null choices handling", vetoMode: false, minMessageLength: 0);
        var nullChoicesResponse = @"{""choices"": null}";
        MockOpenAISuccessResponse(nullChoicesResponse);

        // Act
        var response = await _sut.CheckAsync(request);

        // Assert
        Assert.That(response.Result, Is.EqualTo(CheckResultType.Clean));
        Assert.That(response.Details, Does.Contain("Invalid OpenAI response"));
    }

    [Test]
    public async Task CheckAsync_WithEmptyContent_ReturnsClean()
    {
        // Arrange
        var request = CreateOpenAICheckRequest("Test message for empty content handling", vetoMode: false, minMessageLength: 0);
        var emptyContentResponse = @"{
            ""choices"": [{
                ""message"": {
                    ""role"": ""assistant"",
                    ""content"": """"
                }
            }]
        }";
        MockOpenAISuccessResponse(emptyContentResponse);

        // Act
        var response = await _sut.CheckAsync(request);

        // Assert
        Assert.That(response.Result, Is.EqualTo(CheckResultType.Clean));
        Assert.That(response.Details, Does.Contain("Empty OpenAI response"));
    }

    [Test]
    public async Task CheckAsync_WithMissingApiKey_ReturnsCleanWithError()
    {
        // Arrange
        var request = CreateOpenAICheckRequest("Test message for missing API key handling", vetoMode: false, apiKey: "", minMessageLength: 0);

        // Act
        var response = await _sut.CheckAsync(request);

        // Assert
        Assert.That(response.Result, Is.EqualTo(CheckResultType.Clean));
        Assert.That(response.Details, Does.Contain("OpenAI API key not configured"));
        Assert.That(response.Confidence, Is.EqualTo(0));
        Assert.That(response.Error, Is.TypeOf<InvalidOperationException>());
    }

    [Test]
    public async Task CheckAsync_WithNetworkTimeout_ReturnsCleanAndFailsOpen()
    {
        // Arrange
        _mockServer
            .Given(Request.Create().WithPath("/chat/completions").UsingPost())
            .RespondWith(Response.Create()
                .WithDelay(TimeSpan.FromSeconds(10)) // Longer than CancellationToken timeout
                .WithStatusCode(HttpStatusCode.OK));

        // Create a short timeout token
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var request = CreateOpenAICheckRequest(
            message: "Test message for network timeout handling",
            vetoMode: false,
            hasSpamFlags: false,
            systemPrompt: null,
            minMessageLength: 0,
            model: "gpt-4",
            cancellationToken: cts.Token);

        // Act
        var response = await _sut.CheckAsync(request);

        // Assert
        Assert.That(response.Result, Is.EqualTo(CheckResultType.Clean), "Should fail open on timeout");
        Assert.That(response.Details, Does.Contain("OpenAI check timed out - allowing message"));
        Assert.That(response.Confidence, Is.EqualTo(0));
        Assert.That(response.Error, Is.TypeOf<TimeoutException>());
    }

    #endregion

    #region Cache Behavior Tests

    [Test]
    public async Task CheckAsync_WithCachedResult_UsesCache()
    {
        // Arrange
        var request = CreateOpenAICheckRequest("Test message for caching behavior validation", vetoMode: false, minMessageLength: 0);
        MockSuccessfulJsonResponse("spam", "Cached spam detection", 0.9);

        // Act - First call populates cache
        var firstResponse = await _sut.CheckAsync(request);

        // Clear mock server logs to verify no second call
        _mockServer.Reset();
        MockSuccessfulJsonResponse("clean", "This should not be returned", 0.5);

        // Act - Second call should use cache
        var secondResponse = await _sut.CheckAsync(request);

        // Assert
        Assert.That(firstResponse.Result, Is.EqualTo(CheckResultType.Spam));
        Assert.That(firstResponse.Details, Does.Not.Contain("(cached)"));

        Assert.That(secondResponse.Result, Is.EqualTo(CheckResultType.Spam), "Should return cached result");
        Assert.That(secondResponse.Details, Does.Contain("(cached)"));
        Assert.That(secondResponse.Details, Does.Contain("Cached spam detection"));
        Assert.That(secondResponse.Confidence, Is.EqualTo(90));

        // Verify only one API call was made (the first one)
        Assert.That(_mockServer.LogEntries.Count(), Is.EqualTo(0), "No API call should be made on cache hit");
    }

    [Test]
    public async Task CheckAsync_WithDifferentMessages_UsesDistinctCacheKeys()
    {
        // Arrange
        var request1 = CreateOpenAICheckRequest("First message for cache key testing", vetoMode: false, minMessageLength: 0);
        var request2 = CreateOpenAICheckRequest("Second message for cache key testing", vetoMode: false, minMessageLength: 0);

        MockSuccessfulJsonResponse("spam", "First is spam", 0.9);

        // Act - First call
        var response1 = await _sut.CheckAsync(request1);

        // Reconfigure mock for second message
        _mockServer.Reset();
        MockSuccessfulJsonResponse("clean", "Second is clean", 0.8);

        // Act - Second call with different message
        var response2 = await _sut.CheckAsync(request2);

        // Assert
        Assert.That(response1.Result, Is.EqualTo(CheckResultType.Spam));
        Assert.That(response1.Details, Does.Contain("First is spam"));

        Assert.That(response2.Result, Is.EqualTo(CheckResultType.Clean));
        Assert.That(response2.Details, Does.Contain("Second is clean"));
    }

    #endregion

    #region Message Length Tests

    [Test]
    public async Task CheckAsync_WithShortMessage_SkipsCheckWhenNotEnabled()
    {
        // Arrange
        var request = CreateOpenAICheckRequest(
            "Hi",
            vetoMode: false,
            minMessageLength: 10,
            checkShortMessages: false);

        // Act
        var response = await _sut.CheckAsync(request);

        // Assert
        Assert.That(response.Result, Is.EqualTo(CheckResultType.Clean));
        Assert.That(response.Details, Does.Contain("Message too short"));
        Assert.That(response.Details, Does.Contain("< 10 chars"));
        Assert.That(response.Confidence, Is.EqualTo(0));

        // Verify no API call
        Assert.That(_mockServer.LogEntries.Count(), Is.EqualTo(0));
    }

    [Test]
    public async Task CheckAsync_WithShortMessage_ChecksWhenEnabled()
    {
        // Arrange
        var request = CreateOpenAICheckRequest(
            "Hi",
            vetoMode: false,
            minMessageLength: 10,
            checkShortMessages: true);
        MockSuccessfulJsonResponse("clean", "Short but legitimate", 0.7);

        // Act
        var response = await _sut.CheckAsync(request);

        // Assert
        Assert.That(response.Result, Is.EqualTo(CheckResultType.Clean));
        Assert.That(response.Details, Does.Contain("Short but legitimate"));

        // Verify API call was made
        Assert.That(_mockServer.LogEntries.Count(), Is.EqualTo(1));
    }

    [Test]
    public async Task CheckAsync_WithMessageAtMinLength_PerformsCheck()
    {
        // Arrange
        var request = CreateOpenAICheckRequest(
            "Exactly10",
            vetoMode: false,
            minMessageLength: 9,
            checkShortMessages: false);
        MockSuccessfulJsonResponse("clean", "At minimum length", 0.8);

        // Act
        var response = await _sut.CheckAsync(request);

        // Assert
        Assert.That(response.Result, Is.EqualTo(CheckResultType.Clean));
        Assert.That(_mockServer.LogEntries.Count(), Is.EqualTo(1));
    }

    #endregion

    #region History Context Tests

    [Test]
    public async Task CheckAsync_WithMessageHistory_IncludesContextInRequest()
    {
        // Arrange
        var historyMessages = new[]
        {
            new HistoryMessage
            {
                UserId = "user1",
                UserName = "Alice",
                Message = "Previous message from Alice",
                Timestamp = DateTime.UtcNow.AddMinutes(-5),
                WasSpam = false
            },
            new HistoryMessage
            {
                UserId = "user2",
                UserName = "Bob",
                Message = "Spam message from Bob with crypto promotion",
                Timestamp = DateTime.UtcNow.AddMinutes(-3),
                WasSpam = true
            }
        };

        _messageHistoryService
            .GetRecentMessagesAsync(123456, 5, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<HistoryMessage>>(historyMessages));

        var request = CreateOpenAICheckRequest("Current message", vetoMode: false, chatId: 123456);
        MockSuccessfulJsonResponse("clean", "Contextual analysis", 0.85);

        // Act
        var response = await _sut.CheckAsync(request);

        // Assert
        Assert.That(response.Result, Is.EqualTo(CheckResultType.Clean));

        // Verify history service was called
        await _messageHistoryService.Received(1).GetRecentMessagesAsync(123456, 5, Arg.Any<CancellationToken>());

        // Verify API request contains history context
        var logEntry = _mockServer.LogEntries.First();
        var requestBody = logEntry.RequestMessage.Body;
        Assert.That(requestBody, Does.Contain("Recent message history"));
        Assert.That(requestBody, Does.Contain("[OK] Alice:"));
        Assert.That(requestBody, Does.Contain("[SPAM] Bob:"));
    }

    [Test]
    public async Task CheckAsync_WithNoHistory_StillPerformsCheck()
    {
        // Arrange
        _messageHistoryService
            .GetRecentMessagesAsync(Arg.Any<long>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Enumerable.Empty<HistoryMessage>()));

        var request = CreateOpenAICheckRequest("Test message", vetoMode: false);
        MockSuccessfulJsonResponse("clean", "No history available", 0.8);

        // Act
        var response = await _sut.CheckAsync(request);

        // Assert
        Assert.That(response.Result, Is.EqualTo(CheckResultType.Clean));
        Assert.That(_mockServer.LogEntries.Count(), Is.EqualTo(1));
    }

    #endregion

    #region Confidence Calculation Tests

    [Test]
    [TestCase(0.0, 0)]
    [TestCase(0.25, 25)]
    [TestCase(0.5, 50)]
    [TestCase(0.75, 75)]
    [TestCase(0.95, 95)]
    [TestCase(1.0, 100)]
    public async Task CheckAsync_WithVariousConfidences_ConvertsToPercentCorrectly(double confidence, int expectedPercent)
    {
        // Arrange
        var request = CreateOpenAICheckRequest("Test message", vetoMode: false);
        MockSuccessfulJsonResponse("spam", "Test confidence conversion", confidence);

        // Act
        var response = await _sut.CheckAsync(request);

        // Assert
        Assert.That(response.Confidence, Is.EqualTo(expectedPercent));
    }

    [Test]
    public async Task CheckAsync_WithConfidenceRounding_RoundsCorrectly()
    {
        // Arrange
        var request = CreateOpenAICheckRequest("Test message", vetoMode: false);
        MockSuccessfulJsonResponse("spam", "Test rounding", 0.845); // 0.845 * 100 = 84.5 → 84 (banker's rounding)

        // Act
        var response = await _sut.CheckAsync(request);

        // Assert
        Assert.That(response.Confidence, Is.EqualTo(84));
    }

    #endregion

    #region Custom Prompt Tests

    [Test]
    public async Task CheckAsync_WithCustomPrompt_UsesCustomRules()
    {
        // Arrange
        var customPrompt = "Custom rule: Flag messages containing 'test' as spam.";
        var request = CreateOpenAICheckRequest(
            "This is a test message",
            vetoMode: false,
            systemPrompt: customPrompt);
        MockSuccessfulJsonResponse("spam", "Matches custom rule", 0.9);

        // Act
        var response = await _sut.CheckAsync(request);

        // Assert
        Assert.That(response.Result, Is.EqualTo(CheckResultType.Spam));

        // Verify custom prompt was included in request (check for escaped version in JSON)
        var logEntry = _mockServer.LogEntries.First();
        var requestBody = logEntry.RequestMessage.Body;
        // Custom prompt should appear in the system message content
        Assert.That(requestBody, Does.Contain("Custom rule:"));
        Assert.That(requestBody, Does.Contain("Flag messages containing"));
    }

    [Test]
    public async Task CheckAsync_WithoutCustomPrompt_UsesDefaultRules()
    {
        // Arrange
        var request = CreateOpenAICheckRequest("Test message", vetoMode: false, systemPrompt: null);
        MockSuccessfulJsonResponse("clean", "Default rules applied", 0.8);

        // Act
        var response = await _sut.CheckAsync(request);

        // Assert
        Assert.That(response.Result, Is.EqualTo(CheckResultType.Clean));

        // Verify default rules are in request
        var logEntry = _mockServer.LogEntries.First();
        var requestBody = logEntry.RequestMessage.Body;
        Assert.That(requestBody, Does.Contain("SPAM indicators"));
        Assert.That(requestBody, Does.Contain("LEGITIMATE content"));
    }

    #endregion

    #region API Request Format Tests

    [Test]
    public async Task CheckAsync_SendsCorrectRequestFormat()
    {
        // Arrange
        var request = CreateOpenAICheckRequest(
            "Test message",
            vetoMode: false,
            model: "gpt-4",
            maxTokens: 150);
        MockSuccessfulJsonResponse("clean", "Valid request", 0.8);

        // Act
        await _sut.CheckAsync(request);

        // Assert
        var logEntry = _mockServer.LogEntries.First();
        var requestBody = logEntry.RequestMessage.Body;

        Assert.That(requestBody, Does.Contain("\"model\":\"gpt-4\""));
        Assert.That(requestBody, Does.Contain("\"max_tokens\":150"));
        Assert.That(requestBody, Does.Contain("\"temperature\":0.1"));
        Assert.That(requestBody, Does.Contain("\"top_p\":1"));
        Assert.That(requestBody, Does.Contain("\"response_format\""));
        Assert.That(requestBody, Does.Contain("json_object"));

        // Verify messages structure
        Assert.That(requestBody, Does.Contain("\"role\":\"system\""));
        Assert.That(requestBody, Does.Contain("\"role\":\"user\""));
    }

    [Test]
    public async Task CheckAsync_IncludesUserInfoInPrompt()
    {
        // Arrange
        var request = CreateOpenAICheckRequest(
            "Test message",
            vetoMode: false,
            userId: 789012,
            userName: "TestUser");
        MockSuccessfulJsonResponse("clean", "User info included", 0.8);

        // Act
        await _sut.CheckAsync(request);

        // Assert
        var logEntry = _mockServer.LogEntries.First();
        var requestBody = logEntry.RequestMessage.Body;

        Assert.That(requestBody, Does.Contain("user TestUser"));
        Assert.That(requestBody, Does.Contain("ID: 789012"));
    }

    #endregion

    #region Helper Methods

    private static ContentCheckRequest CreateCheckRequest(string message)
    {
        return new ContentCheckRequest
        {
            Message = message,
            UserId = 12345,
            UserName = "TestUser",
            ChatId = 67890
        };
    }

    private static OpenAICheckRequest CreateOpenAICheckRequest(
        string message,
        bool vetoMode,
        bool hasSpamFlags = false,
        string? systemPrompt = null,
        int minMessageLength = 0,
        bool checkShortMessages = false,
        string apiKey = "test-api-key",
        string model = "gpt-3.5-turbo",
        int maxTokens = 100,
        long chatId = 67890,
        long userId = 12345,
        string? userName = "TestUser",
        CancellationToken cancellationToken = default)
    {
        return new OpenAICheckRequest
        {
            Message = message,
            UserId = userId,
            UserName = userName,
            ChatId = chatId,
            CancellationToken = cancellationToken == default ? CancellationToken.None : cancellationToken,
            VetoMode = vetoMode,
            SystemPrompt = systemPrompt,
            HasSpamFlags = hasSpamFlags,
            MinMessageLength = minMessageLength,
            CheckShortMessages = checkShortMessages,
            ApiKey = apiKey,
            Model = model,
            MaxTokens = maxTokens
        };
    }

    private void MockSuccessfulJsonResponse(string result, string reason, double confidence)
    {
        // Create the inner JSON response that OpenAI returns in the content field
        var innerJsonResponse = new
        {
            result = result,
            reason = reason,
            confidence = confidence
        };
        var innerJson = JsonSerializer.Serialize(innerJsonResponse);

        // Create the full OpenAI API response structure using actual types
        var response = new OpenAIResponse
        {
            Choices = new[]
            {
                new OpenAIChoice
                {
                    Message = new OpenAIMessage
                    {
                        Role = "assistant",
                        Content = innerJson
                    },
                    FinishReason = "stop"
                }
            },
            Usage = new OpenAIUsage
            {
                PromptTokens = 100,
                CompletionTokens = 50,
                TotalTokens = 150
            }
        };

        var responseJson = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        MockOpenAISuccessResponse(responseJson);
    }

    private void MockOpenAISuccessResponse(string content)
    {
        // Check if content is already a full OpenAI response (contains "choices")
        var responseBody = content.Contains("\"choices\"")
            ? content
            : $@"{{
                ""choices"": [{{
                    ""message"": {{
                        ""role"": ""assistant"",
                        ""content"": ""{EscapeJson(content)}""
                    }},
                    ""finish_reason"": ""stop""
                }}]
            }}";

        _mockServer
            .Given(Request.Create()
                .WithPath("/chat/completions")
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(HttpStatusCode.OK)
                .WithHeader("Content-Type", "application/json")
                .WithBody(responseBody));
    }

    private static string EscapeJson(string text)
    {
        return text
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }

    #endregion
}
