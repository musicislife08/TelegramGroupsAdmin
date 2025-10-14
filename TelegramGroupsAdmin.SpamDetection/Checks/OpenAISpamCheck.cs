using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.SpamDetection.Abstractions;
using TelegramGroupsAdmin.SpamDetection.Configuration;
using TelegramGroupsAdmin.SpamDetection.Models;
using TelegramGroupsAdmin.SpamDetection.Repositories;
using TelegramGroupsAdmin.SpamDetection.Services;

namespace TelegramGroupsAdmin.SpamDetection.Checks;

/// <summary>
/// Enhanced OpenAI spam check with history context, JSON responses, and fallback
/// Improved veto system based on tg-spam with additional context and reliability
/// </summary>
public class OpenAISpamCheck : ISpamCheck
{
    private readonly ILogger<OpenAISpamCheck> _logger;
    private readonly ISpamDetectionConfigRepository _configRepository;
    private readonly OpenAIOptions _openAIOptions;
    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly IMessageHistoryService _messageHistoryService;

    public string CheckName => "OpenAI";

    public OpenAISpamCheck(
        ILogger<OpenAISpamCheck> logger,
        ISpamDetectionConfigRepository configRepository,
        IOptions<OpenAIOptions> openAIOptions,
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache,
        IMessageHistoryService messageHistoryService)
    {
        _logger = logger;
        _configRepository = configRepository;
        _openAIOptions = openAIOptions.Value;
        _httpClient = httpClientFactory.CreateClient();
        _cache = cache;
        _messageHistoryService = messageHistoryService;

        // Configure HTTP client for OpenAI API
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "TelegramGroupsAdmin/1.0");
    }

    /// <summary>
    /// Check if OpenAI check should be executed
    /// </summary>
    public bool ShouldExecute(SpamCheckRequest request)
    {
        // Skip empty messages
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return false;
        }

        // Check if enabled is done in CheckAsync since we need to load config from DB
        return true;
    }

    /// <summary>
    /// Execute OpenAI spam check
    /// </summary>
    public async Task<SpamCheckResponse> CheckAsync(SpamCheckRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            // Load config from database
            var config = await _configRepository.GetGlobalConfigAsync(cancellationToken);

            // Check if this check is enabled
            if (!config.OpenAI.Enabled)
            {
                return new SpamCheckResponse
                {
                    CheckName = CheckName,
                    IsSpam = false,
                    Details = "Check disabled",
                    Confidence = 0
                };
            }

            // Skip short messages unless specifically configured to check them
            if (!config.OpenAI.CheckShortMessages && request.Message.Length < config.MinMessageLength)
            {
                return new SpamCheckResponse
                {
                    CheckName = CheckName,
                    IsSpam = false,
                    Details = $"Message too short (< {config.MinMessageLength} chars)",
                    Confidence = 0
                };
            }

            // In veto mode, only run if other checks flagged as spam
            if (config.OpenAI.VetoMode && !request.HasSpamFlags)
            {
                return new SpamCheckResponse
                {
                    CheckName = CheckName,
                    IsSpam = false,
                    Details = "Veto mode: no spam flags from other checks",
                    Confidence = 0
                };
            }

            // Check cache first
            var cacheKey = $"openai_check_{GetMessageHash(request.Message)}";
            if (_cache.TryGetValue(cacheKey, out OpenAIResponse? cachedResponse) && cachedResponse != null)
            {
                _logger.LogDebug("OpenAI check for user {UserId}: Using cached result", request.UserId);
                return CreateResponse(cachedResponse, config, fromCache: true);
            }

            // Check API key
            if (string.IsNullOrEmpty(_openAIOptions.ApiKey))
            {
                _logger.LogWarning("OpenAI API key not configured");
                return new SpamCheckResponse
                {
                    CheckName = CheckName,
                    IsSpam = false,
                    Details = "OpenAI API key not configured",
                    Confidence = 0,
                    Error = new InvalidOperationException("OpenAI API key not configured")
                };
            }

            // Prepare the API request with history context
            var apiRequest = await CreateOpenAIRequestAsync(request, config, cancellationToken);
            var requestJson = JsonSerializer.Serialize(apiRequest, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
            });

            _logger.LogDebug("OpenAI check for user {UserId}: Calling API with message length {MessageLength}",
                request.UserId, request.Message.Length);

            // Make API call
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
            httpRequest.Headers.Add("Authorization", $"Bearer {_openAIOptions.ApiKey}");
            httpRequest.Content = new StringContent(requestJson, System.Text.Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("OpenAI API returned {StatusCode}: {ErrorContent}", response.StatusCode, errorContent);

                // Handle rate limiting specially
                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    return new SpamCheckResponse
                    {
                        CheckName = CheckName,
                        IsSpam = false, // Fail open during rate limits
                        Details = "OpenAI API rate limited - allowing message",
                        Confidence = 0,
                        Error = new HttpRequestException($"OpenAI API rate limited: {response.StatusCode}")
                    };
                }

                return new SpamCheckResponse
                {
                    CheckName = CheckName,
                    IsSpam = false,
                    Details = $"OpenAI API error: {response.StatusCode}",
                    Confidence = 0,
                    Error = new HttpRequestException($"OpenAI API error: {response.StatusCode}")
                };
            }

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            var openaiResponse = JsonSerializer.Deserialize<OpenAIResponse>(responseJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (openaiResponse?.Choices?.Any() != true)
            {
                _logger.LogWarning("Invalid OpenAI API response for user {UserId}", request.UserId);
                return new SpamCheckResponse
                {
                    CheckName = CheckName,
                    IsSpam = false,
                    Details = "Invalid OpenAI response",
                    Confidence = 0,
                    Error = new InvalidOperationException("Invalid OpenAI response format")
                };
            }

            // Cache the result for 1 hour
            _cache.Set(cacheKey, openaiResponse, TimeSpan.FromHours(1));

            return CreateResponse(openaiResponse, config, fromCache: false);
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("OpenAI check for user {UserId}: Request timed out", request.UserId);
            return new SpamCheckResponse
            {
                CheckName = CheckName,
                IsSpam = false, // Fail open on timeout
                Details = "OpenAI check timed out - allowing message",
                Confidence = 0,
                Error = new TimeoutException("OpenAI API request timed out")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenAI check for user {UserId}: Unexpected error", request.UserId);
            return new SpamCheckResponse
            {
                CheckName = CheckName,
                IsSpam = false, // Fail open on error
                Details = "OpenAI check failed due to error",
                Confidence = 0,
                Error = ex
            };
        }
    }

    /// <summary>
    /// Create enhanced OpenAI API request with history context and JSON response format
    /// </summary>
    private async Task<OpenAIRequest> CreateOpenAIRequestAsync(SpamCheckRequest request, SpamDetectionConfig config, CancellationToken cancellationToken)
    {
        var systemPrompt = config.OpenAI.SystemPrompt ?? GetDefaultSystemPrompt(config.OpenAI.VetoMode);

        // Get message history for context
        var history = await _messageHistoryService.GetRecentMessagesAsync(request.ChatId ?? "unknown", 5, cancellationToken);
        var contextBuilder = new System.Text.StringBuilder();

        if (history.Any())
        {
            contextBuilder.AppendLine("\nRecent message history for context:");
            foreach (var msg in history.Take(3))
            {
                var status = msg.WasSpam ? "[SPAM]" : "[OK]";
                contextBuilder.AppendLine($"{status} {msg.UserName}: {msg.Message.Substring(0, Math.Min(100, msg.Message.Length))}");
            }
            contextBuilder.AppendLine();
        }

        var userPrompt = config.OpenAI.VetoMode
            ? $"Analyze this message that was flagged by other spam filters. Is it actually spam?\n\n{contextBuilder}\nCurrent message from user {request.UserName} (ID: {request.UserId}):\n\"{request.Message}\"\n\nRespond with JSON: {{\"is_spam\": true/false, \"reason\": \"explanation\", \"confidence\": 0.0-1.0}}"
            : $"Analyze this message for spam content.\n\n{contextBuilder}\nMessage from user {request.UserName} (ID: {request.UserId}):\n\"{request.Message}\"\n\nRespond with JSON: {{\"is_spam\": true/false, \"reason\": \"explanation\", \"confidence\": 0.0-1.0}}";

        return new OpenAIRequest
        {
            Model = _openAIOptions.Model,
            Messages =
            [
                new OpenAIMessage { Role = "system", Content = systemPrompt },
                new OpenAIMessage { Role = "user", Content = userPrompt }
            ],
            MaxTokens = _openAIOptions.MaxTokens,
            Temperature = 0.1,
            TopP = 1.0,
            ResponseFormat = new { type = "json_object" }
        };
    }

    /// <summary>
    /// Get default system prompt based on mode (enhanced with JSON format)
    /// </summary>
    private static string GetDefaultSystemPrompt(bool vetoMode)
    {
        if (vetoMode)
        {
            return """
                You are a spam verification system. Other filters have flagged this message as potential spam,
                but you need to verify if it's actually spam or a false positive.

                Consider the message context, user history, and conversation flow.

                Spam indicators: promotional content, scams, adult content, cryptocurrency schemes, gambling,
                fake opportunities, suspicious links, repetitive content, or obvious rule violations.

                Be conservative - when in doubt, mark as NOT spam to avoid censoring legitimate messages.
                Provide clear reasoning for your decision.

                Always respond with valid JSON format.
                """;
        }

        return """
            You are a spam detection system for a Telegram group. Analyze the message and determine if it's spam.

            Consider the conversation context and user behavior patterns.

            Spam indicators: promotional content, scams, cryptocurrency schemes, adult content, gambling,
            fake opportunities, suspicious links, or content that violates group rules.

            Be conservative - when in doubt, choose NOT spam to preserve legitimate conversation.
            Provide clear reasoning for your decision.

            Always respond with valid JSON format.
            """;
    }

    /// <summary>
    /// Create spam check response from OpenAI API response with JSON parsing and fallback
    /// </summary>
    private SpamCheckResponse CreateResponse(OpenAIResponse openaiResponse, SpamDetectionConfig config, bool fromCache)
    {
        var choice = openaiResponse.Choices?.FirstOrDefault();
        var content = choice?.Message?.Content?.Trim();

        if (string.IsNullOrEmpty(content))
        {
            return CreateFallbackResponse("Empty OpenAI response", fromCache);
        }

        // Try to parse JSON response
        try
        {
            var jsonResponse = JsonSerializer.Deserialize<OpenAIJsonResponse>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (jsonResponse != null)
            {
                var confidence = (int)Math.Round((jsonResponse.Confidence ?? 0.8) * 100);
                var details = config.OpenAI.VetoMode
                    ? (jsonResponse.IsSpam ? $"OpenAI confirmed spam: {jsonResponse.Reason}" : $"OpenAI vetoed spam: {jsonResponse.Reason}")
                    : (jsonResponse.IsSpam ? $"OpenAI detected spam: {jsonResponse.Reason}" : $"OpenAI found no spam: {jsonResponse.Reason}");

                if (fromCache)
                {
                    details += " (cached)";
                }

                _logger.LogDebug("OpenAI check completed: IsSpam={IsSpam}, Confidence={Confidence}, Reason={Reason}, FromCache={FromCache}",
                    jsonResponse.IsSpam, confidence, jsonResponse.Reason, fromCache);

                return new SpamCheckResponse
                {
                    CheckName = CheckName,
                    IsSpam = jsonResponse.IsSpam,
                    Details = details,
                    Confidence = confidence // Use confidence for both spam and non-spam (veto has high confidence too)
                };
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse OpenAI JSON response: {Content}", content);
        }

        // Fallback to legacy parsing if JSON fails
        return CreateLegacyResponse(content, config, fromCache);
    }

    /// <summary>
    /// Create fallback response when OpenAI fails
    /// </summary>
    private SpamCheckResponse CreateFallbackResponse(string reason, bool fromCache)
    {
        var details = $"OpenAI fallback: {reason} - allowing message";
        if (fromCache)
        {
            details += " (cached)";
        }

        return new SpamCheckResponse
        {
            CheckName = CheckName,
            IsSpam = false, // Fail open
            Details = details,
            Confidence = 0
        };
    }

    /// <summary>
    /// Legacy response parsing for non-JSON responses
    /// </summary>
    private SpamCheckResponse CreateLegacyResponse(string content, SpamDetectionConfig config, bool fromCache)
    {
        var upperContent = content.ToUpperInvariant();
        var isSpam = upperContent.Contains("SPAM") && !upperContent.Contains("NOT_SPAM");
        var confidence = isSpam ? 75 : 0; // Lower confidence for legacy parsing

        var details = config.OpenAI.VetoMode
            ? (isSpam ? "OpenAI confirmed spam (legacy)" : "OpenAI vetoed spam (legacy)")
            : (isSpam ? "OpenAI detected spam (legacy)" : "OpenAI found no spam (legacy)");

        if (fromCache)
        {
            details += " (cached)";
        }

        _logger.LogDebug("OpenAI legacy parsing: IsSpam={IsSpam}, Confidence={Confidence}, Content={Content}",
            isSpam, confidence, content.Length > 100 ? content[..100] + "..." : content);

        return new SpamCheckResponse
        {
            CheckName = CheckName,
            IsSpam = isSpam,
            Details = details,
            Confidence = confidence
        };
    }

    /// <summary>
    /// Generate a hash for message caching
    /// </summary>
    private static string GetMessageHash(string message)
    {
        return message.Length.ToString() + "_" + Math.Abs(message.GetHashCode()).ToString();
    }
}

/// <summary>
/// OpenAI API request structure
/// </summary>
internal record OpenAIRequest
{
    public required string Model { get; init; }
    public required OpenAIMessage[] Messages { get; init; }
    public int MaxTokens { get; init; }
    public double Temperature { get; init; }
    public double TopP { get; init; }
    public object? ResponseFormat { get; init; }
}

/// <summary>
/// OpenAI message structure
/// </summary>
internal record OpenAIMessage
{
    public required string Role { get; init; }
    public required string Content { get; init; }
}

/// <summary>
/// OpenAI API response structure
/// </summary>
internal record OpenAIResponse
{
    public OpenAIChoice[]? Choices { get; init; }
    public OpenAIUsage? Usage { get; init; }
}

/// <summary>
/// OpenAI choice structure
/// </summary>
internal record OpenAIChoice
{
    public OpenAIMessage? Message { get; init; }
    public string? FinishReason { get; init; }
}

/// <summary>
/// OpenAI usage structure
/// </summary>
internal record OpenAIUsage
{
    public int PromptTokens { get; init; }
    public int CompletionTokens { get; init; }
    public int TotalTokens { get; init; }
}

/// <summary>
/// OpenAI JSON response structure for enhanced spam detection
/// </summary>
internal record OpenAIJsonResponse
{
    public bool IsSpam { get; init; }
    public string? Reason { get; init; }
    public double? Confidence { get; init; }
}