using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TelegramGroupsAdmin.ContentDetection.Checks;
using TelegramGroupsAdmin.ContentDetection.Constants;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Services;

namespace TelegramGroupsAdmin.Tests.ContentDetection;

/// <summary>
/// Tests for OpenAIContentCheckV2 with V2 scoring model (0.0-5.0 points)
/// </summary>
[TestFixture]
public class OpenAIContentCheckTests
{
    private ILogger<OpenAIContentCheckV2> _mockLogger = null!;
    private IHttpClientFactory _mockHttpClientFactory = null!;
    private IMessageHistoryService _mockMessageHistoryService = null!;
    private IMemoryCache _memoryCache = null!;
    private OpenAIContentCheckV2 _check = null!;
    private TestHttpMessageHandler _httpHandler = null!;

    [SetUp]
    public void Setup()
    {
        _mockLogger = Substitute.For<ILogger<OpenAIContentCheckV2>>();
        _mockHttpClientFactory = Substitute.For<IHttpClientFactory>();
        _mockMessageHistoryService = Substitute.For<IMessageHistoryService>();
        _memoryCache = new MemoryCache(new MemoryCacheOptions());

        // Setup message history service to return empty list by default
        _mockMessageHistoryService
            .GetRecentMessagesAsync(Arg.Any<long>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<HistoryMessage>>(new List<HistoryMessage>()));

        // Setup test HTTP handler
        _httpHandler = new TestHttpMessageHandler();

        _check = new OpenAIContentCheckV2(
            _mockLogger,
            _mockHttpClientFactory,
            _memoryCache,
            _mockMessageHistoryService
        );
    }

    [TearDown]
    public void TearDown()
    {
        _memoryCache.Dispose();
        _httpHandler.Dispose();
    }

    #region ShouldExecute Tests

    [Test]
    public void ShouldExecute_EmptyMessage_ReturnsFalse()
    {
        // Arrange
        var request = new ContentCheckRequest
        {
            Message = "",
            UserId = 123,
            ChatId = 456,
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
            UserId = 123,
            ChatId = 456,
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
            UserId = 123,
            ChatId = 456,
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
            UserId = 123,
            ChatId = 456,
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
            UserId = 123,
            ChatId = 456,
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
        var request = new OpenAICheckRequest
        {
            Message = "Hi",
            UserId = 123,
            UserName = "testuser",
            ChatId = 456,
            VetoMode = false,
            SystemPrompt = null,
            HasSpamFlags = false,
            MinMessageLength = 10,
            CheckShortMessages = false,
            MessageHistoryCount = 3,
            ApiKey = "test-key",
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
        var apiResponse = CreateSpamResponse("Suspicious short message", 0.8);
        SetupHttpClient(apiResponse);

        var request = new OpenAICheckRequest
        {
            Message = "Hi",
            UserId = 123,
            UserName = "testuser",
            ChatId = 456,
            VetoMode = false,
            SystemPrompt = null,
            HasSpamFlags = false,
            MinMessageLength = 10,
            CheckShortMessages = true,
            MessageHistoryCount = 3,
            ApiKey = "test-key",
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

    #region CheckAsync - Veto Mode Tests

    [Test]
    public async Task CheckAsync_VetoMode_NoSpamFlags_Abstains()
    {
        // Arrange
        var request = new OpenAICheckRequest
        {
            Message = "This is a test message",
            UserId = 123,
            UserName = "testuser",
            ChatId = 456,
            VetoMode = true,
            SystemPrompt = null,
            HasSpamFlags = false,
            MinMessageLength = 10,
            CheckShortMessages = false,
            MessageHistoryCount = 3,
            ApiKey = "test-key",
            Model = "gpt-4",
            MaxTokens = 500,
            CancellationToken = CancellationToken.None
        };

        // Act
        var response = await _check.CheckAsync(request);

        // Assert
        Assert.That(response.Score, Is.EqualTo(0.0));
        Assert.That(response.Abstained, Is.True);
        Assert.That(response.Details, Does.Contain("Veto mode"));
    }

    [Test]
    public async Task CheckAsync_VetoMode_HasSpamFlags_CallsAPI()
    {
        // Arrange
        var apiResponse = CreateCleanResponse("Looks fine to me", 0.9);
        SetupHttpClient(apiResponse);

        var request = new OpenAICheckRequest
        {
            Message = "This is a test message",
            UserId = 123,
            UserName = "testuser",
            ChatId = 456,
            VetoMode = true,
            SystemPrompt = null,
            HasSpamFlags = true,
            MinMessageLength = 10,
            CheckShortMessages = false,
            MessageHistoryCount = 3,
            ApiKey = "test-key",
            Model = "gpt-4",
            MaxTokens = 500,
            CancellationToken = CancellationToken.None
        };

        // Act
        var response = await _check.CheckAsync(request);

        // Assert
        Assert.That(response.Score, Is.EqualTo(0.0));
        Assert.That(response.Abstained, Is.True);
        Assert.That(response.Details, Does.Contain("Clean"));
    }

    #endregion

    #region CheckAsync - Missing API Key Tests

    [Test]
    public async Task CheckAsync_MissingApiKey_Abstains()
    {
        // Arrange
        var request = new OpenAICheckRequest
        {
            Message = "This is a test message",
            UserId = 123,
            UserName = "testuser",
            ChatId = 456,
            VetoMode = false,
            SystemPrompt = null,
            HasSpamFlags = false,
            MinMessageLength = 10,
            CheckShortMessages = false,
            MessageHistoryCount = 3,
            ApiKey = "",
            Model = "gpt-4",
            MaxTokens = 500,
            CancellationToken = CancellationToken.None
        };

        // Act
        var response = await _check.CheckAsync(request);

        // Assert
        Assert.That(response.Score, Is.EqualTo(0.0));
        Assert.That(response.Abstained, Is.True);
        Assert.That(response.Details, Does.Contain("API key not configured"));
    }

    #endregion

    #region CheckAsync - Spam Detection Tests

    [Test]
    public async Task CheckAsync_SpamDetected_HighConfidence_ReturnsHighScore()
    {
        // Arrange
        var apiResponse = CreateSpamResponse("This contains prohibited content", 0.95);
        SetupHttpClient(apiResponse);

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
        var apiResponse = CreateSpamResponse("Possibly spam", 0.6);
        SetupHttpClient(apiResponse);

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
        var apiResponse = CreateSpamResponse("Slightly suspicious", 0.3);
        SetupHttpClient(apiResponse);

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
        var apiResponse = CreateReviewResponse("Needs human review", 0.9);
        SetupHttpClient(apiResponse);

        var request = CreateValidRequest();

        // Act
        var response = await _check.CheckAsync(request);

        // Assert
        // Review is capped at 3.0 even though 0.9 * 5.0 = 4.5
        Assert.That(response.Score, Is.EqualTo(3.0).Within(0.01));
        Assert.That(response.Abstained, Is.False);
        Assert.That(response.Details, Does.Contain("Review"));
    }

    [Test]
    public async Task CheckAsync_ReviewResult_MediumConfidence_ReturnsScore()
    {
        // Arrange
        var apiResponse = CreateReviewResponse("Uncertain", 0.5);
        SetupHttpClient(apiResponse);

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
    public async Task CheckAsync_CleanResult_Abstains()
    {
        // Arrange
        var apiResponse = CreateCleanResponse("This is a legitimate message", 0.85);
        SetupHttpClient(apiResponse);

        var request = CreateValidRequest();

        // Act
        var response = await _check.CheckAsync(request);

        // Assert
        Assert.That(response.Score, Is.EqualTo(0.0));
        Assert.That(response.Abstained, Is.True);
        Assert.That(response.Details, Does.Contain("Clean"));
        Assert.That(response.Details, Does.Contain("legitimate message"));
    }

    #endregion

    #region CheckAsync - Caching Tests

    [Test]
    public async Task CheckAsync_SecondCall_UsesCachedResult()
    {
        // Arrange
        var apiResponse = CreateSpamResponse("Spam detected", 0.8);
        SetupHttpClient(apiResponse);

        var request = CreateValidRequest();

        // Act - First call
        var response1 = await _check.CheckAsync(request);
        // Act - Second call with same message
        var response2 = await _check.CheckAsync(request);

        // Assert
        Assert.That(response1.Score, Is.EqualTo(response2.Score));
        Assert.That(response2.Details, Does.Contain("cached"));

        // Verify HTTP client was only called once (cached on second call)
        Assert.That(_httpHandler.RequestCount, Is.EqualTo(1));
    }

    #endregion

    #region CheckAsync - Error Handling Tests

    [Test]
    public async Task CheckAsync_ApiReturnsError_Abstains()
    {
        // Arrange
        SetupHttpClientError(HttpStatusCode.InternalServerError, "Server error");

        var request = CreateValidRequest();

        // Act
        var response = await _check.CheckAsync(request);

        // Assert
        Assert.That(response.Score, Is.EqualTo(0.0));
        Assert.That(response.Abstained, Is.True);
        Assert.That(response.Details, Does.Contain("API error"));
    }

    [Test]
    public async Task CheckAsync_ApiRateLimited_Abstains()
    {
        // Arrange
        SetupHttpClientError(HttpStatusCode.TooManyRequests, "Rate limit exceeded");

        var request = CreateValidRequest();

        // Act
        var response = await _check.CheckAsync(request);

        // Assert
        Assert.That(response.Score, Is.EqualTo(0.0));
        Assert.That(response.Abstained, Is.True);
        Assert.That(response.Details, Does.Contain("rate limited"));
    }

    [Test]
    public async Task CheckAsync_Timeout_Abstains()
    {
        // Arrange
        _httpHandler.SetException(new TaskCanceledException("Request timed out"));

        var httpClient = new HttpClient(_httpHandler)
        {
            BaseAddress = new Uri("https://api.openai.com/v1/")
        };

        _mockHttpClientFactory
            .CreateClient("OpenAI")
            .Returns(httpClient);

        var request = CreateValidRequest();

        // Act
        var response = await _check.CheckAsync(request);

        // Assert
        Assert.That(response.Score, Is.EqualTo(0.0));
        Assert.That(response.Abstained, Is.True);
        Assert.That(response.Details, Does.Contain("timed out"));
    }

    [Test]
    public async Task CheckAsync_InvalidJsonResponse_Abstains()
    {
        // Arrange
        var invalidResponse = new OpenAIResponse
        {
            Choices = new[]
            {
                new OpenAIChoice
                {
                    Message = new OpenAIMessage
                    {
                        Role = "assistant",
                        Content = "This is not valid JSON"
                    }
                }
            }
        };

        SetupHttpClient(invalidResponse);

        var request = CreateValidRequest();

        // Act
        var response = await _check.CheckAsync(request);

        // Assert
        Assert.That(response.Score, Is.EqualTo(0.0));
        Assert.That(response.Abstained, Is.True);
        Assert.That(response.Details, Does.Contain("Failed to parse OpenAI response"));
    }

    [Test]
    public async Task CheckAsync_EmptyResponse_Abstains()
    {
        // Arrange
        var emptyResponse = new OpenAIResponse
        {
            Choices = Array.Empty<OpenAIChoice>()
        };

        SetupHttpClient(emptyResponse);

        var request = CreateValidRequest();

        // Act
        var response = await _check.CheckAsync(request);

        // Assert
        Assert.That(response.Score, Is.EqualTo(0.0));
        Assert.That(response.Abstained, Is.True);
        Assert.That(response.Details, Does.Contain("Invalid OpenAI response"));
    }

    [Test]
    public async Task CheckAsync_Exception_Abstains()
    {
        // Arrange
        _httpHandler.SetException(new HttpRequestException("Network error"));

        var httpClient = new HttpClient(_httpHandler)
        {
            BaseAddress = new Uri("https://api.openai.com/v1/")
        };

        _mockHttpClientFactory
            .CreateClient("OpenAI")
            .Returns(httpClient);

        var request = CreateValidRequest();

        // Act
        var response = await _check.CheckAsync(request);

        // Assert
        Assert.That(response.Score, Is.EqualTo(0.0));
        Assert.That(response.Abstained, Is.True);
        Assert.That(response.Error, Is.Not.Null);
    }

    #endregion

    #region CheckAsync - Edge Cases

    [Test]
    public async Task CheckAsync_MissingConfidence_UsesDefault()
    {
        // Arrange
        var apiResponse = new OpenAIResponse
        {
            Choices = new[]
            {
                new OpenAIChoice
                {
                    Message = new OpenAIMessage
                    {
                        Role = "assistant",
                        Content = JsonSerializer.Serialize(new
                        {
                            result = "spam",
                            reason = "Test spam"
                            // No confidence field
                        })
                    }
                }
            }
        };

        SetupHttpClient(apiResponse);

        var request = CreateValidRequest();

        // Act
        var response = await _check.CheckAsync(request);

        // Assert
        // Default confidence is 0.8, so score should be 0.8 * 5.0 = 4.0
        Assert.That(response.Score, Is.EqualTo(4.0).Within(0.01));
        Assert.That(response.Abstained, Is.False);
    }

    [Test]
    public async Task CheckAsync_UnknownResult_TreatsAsClean()
    {
        // Arrange
        var apiResponse = new OpenAIResponse
        {
            Choices = new[]
            {
                new OpenAIChoice
                {
                    Message = new OpenAIMessage
                    {
                        Role = "assistant",
                        Content = JsonSerializer.Serialize(new
                        {
                            result = "unknown_value",
                            reason = "Test",
                            confidence = 0.5
                        })
                    }
                }
            }
        };

        SetupHttpClient(apiResponse);

        var request = CreateValidRequest();

        // Act
        var response = await _check.CheckAsync(request);

        // Assert
        Assert.That(response.Score, Is.EqualTo(0.0));
        Assert.That(response.Abstained, Is.True);
    }

    #endregion

    #region Helper Methods

    private OpenAICheckRequest CreateValidRequest()
    {
        return new OpenAICheckRequest
        {
            Message = "This is a test message that is long enough to be checked",
            UserId = 123,
            UserName = "testuser",
            ChatId = 456,
            VetoMode = false,
            SystemPrompt = null,
            HasSpamFlags = false,
            MinMessageLength = 10,
            CheckShortMessages = false,
            MessageHistoryCount = 3,
            ApiKey = "test-api-key",
            Model = "gpt-4",
            MaxTokens = 500,
            CancellationToken = CancellationToken.None
        };
    }

    private OpenAIResponse CreateSpamResponse(string reason, double confidence)
    {
        return new OpenAIResponse
        {
            Choices = new[]
            {
                new OpenAIChoice
                {
                    Message = new OpenAIMessage
                    {
                        Role = "assistant",
                        Content = JsonSerializer.Serialize(new
                        {
                            result = "spam",
                            reason,
                            confidence
                        })
                    }
                }
            }
        };
    }

    private OpenAIResponse CreateCleanResponse(string reason, double confidence)
    {
        return new OpenAIResponse
        {
            Choices = new[]
            {
                new OpenAIChoice
                {
                    Message = new OpenAIMessage
                    {
                        Role = "assistant",
                        Content = JsonSerializer.Serialize(new
                        {
                            result = "clean",
                            reason,
                            confidence
                        })
                    }
                }
            }
        };
    }

    private OpenAIResponse CreateReviewResponse(string reason, double confidence)
    {
        return new OpenAIResponse
        {
            Choices = new[]
            {
                new OpenAIChoice
                {
                    Message = new OpenAIMessage
                    {
                        Role = "assistant",
                        Content = JsonSerializer.Serialize(new
                        {
                            result = "review",
                            reason,
                            confidence
                        })
                    }
                }
            }
        };
    }

    private void SetupHttpClient(OpenAIResponse apiResponse)
    {
        var responseJson = JsonSerializer.Serialize(apiResponse, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        _httpHandler.SetResponse(new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(responseJson)
        });

        var httpClient = new HttpClient(_httpHandler)
        {
            BaseAddress = new Uri("https://api.openai.com/v1/")
        };

        _mockHttpClientFactory
            .CreateClient("OpenAI")
            .Returns(httpClient);
    }

    private void SetupHttpClientError(HttpStatusCode statusCode, string errorMessage)
    {
        _httpHandler.SetResponse(new HttpResponseMessage
        {
            StatusCode = statusCode,
            Content = new StringContent(errorMessage)
        });

        var httpClient = new HttpClient(_httpHandler)
        {
            BaseAddress = new Uri("https://api.openai.com/v1/")
        };

        _mockHttpClientFactory
            .CreateClient("OpenAI")
            .Returns(httpClient);
    }

    #endregion

    #region Test HTTP Handler

    /// <summary>
    /// Test HTTP message handler that allows configuring responses and exceptions
    /// </summary>
    private class TestHttpMessageHandler : HttpMessageHandler
    {
        private HttpResponseMessage? _response;
        private Exception? _exception;
        public int RequestCount { get; private set; }

        public void SetResponse(HttpResponseMessage response)
        {
            _response = response;
            _exception = null;
        }

        public void SetException(Exception exception)
        {
            _exception = exception;
            _response = null;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;

            if (_exception != null)
            {
                throw _exception;
            }

            if (_response != null)
            {
                return Task.FromResult(_response);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    #endregion
}
