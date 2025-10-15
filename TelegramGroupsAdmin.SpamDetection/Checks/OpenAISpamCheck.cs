using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.SpamDetection.Abstractions;
using TelegramGroupsAdmin.SpamDetection.Models;
using TelegramGroupsAdmin.SpamDetection.Services;

namespace TelegramGroupsAdmin.SpamDetection.Checks;

/// <summary>
/// Enhanced OpenAI spam check with history context, JSON responses, and fallback
/// Improved veto system based on tg-spam with additional context and reliability
/// </summary>
public class OpenAISpamCheck : ISpamCheck
{
    private readonly ILogger<OpenAISpamCheck> _logger;
    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly IMessageHistoryService _messageHistoryService;

    public string CheckName => "OpenAI";

    public OpenAISpamCheck(
        ILogger<OpenAISpamCheck> logger,
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache,
        IMessageHistoryService messageHistoryService)
    {
        _logger = logger;
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
    public async Task<SpamCheckResponse> CheckAsync(SpamCheckRequestBase request)
    {
        var req = (OpenAICheckRequest)request;

        try
        {
            // Skip short messages unless specifically configured to check them
            if (!req.CheckShortMessages && req.Message.Length < req.MinMessageLength)
            {
                return new SpamCheckResponse
                {
                    CheckName = CheckName,
                    Result = SpamCheckResultType.Clean,
                    Details = $"Message too short (< {req.MinMessageLength} chars)",
                    Confidence = 0
                };
            }

            // In veto mode, only run if other checks flagged as spam
            if (req.VetoMode && !req.HasSpamFlags)
            {
                return new SpamCheckResponse
                {
                    CheckName = CheckName,
                    Result = SpamCheckResultType.Clean,
                    Details = "Veto mode: no spam flags from other checks",
                    Confidence = 0
                };
            }

            // Check cache first
            var cacheKey = $"openai_check_{GetMessageHash(req.Message)}";
            if (_cache.TryGetValue(cacheKey, out OpenAIResponse? cachedResponse) && cachedResponse != null)
            {
                _logger.LogDebug("OpenAI check for user {UserId}: Using cached result", req.UserId);
                return CreateResponse(cachedResponse, req, fromCache: true);
            }

            // Check API key
            if (string.IsNullOrEmpty(req.ApiKey))
            {
                _logger.LogWarning("OpenAI API key not configured");
                return new SpamCheckResponse
                {
                    CheckName = CheckName,
                    Result = SpamCheckResultType.Clean,
                    Details = "OpenAI API key not configured",
                    Confidence = 0,
                    Error = new InvalidOperationException("OpenAI API key not configured")
                };
            }

            // Prepare the API request with history context
            var apiRequest = await CreateOpenAIRequestAsync(req);
            var requestJson = JsonSerializer.Serialize(apiRequest, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
            });

            _logger.LogDebug("OpenAI check for user {UserId}: Calling API with message length {MessageLength}",
                req.UserId, req.Message.Length);

            // Make API call
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
            httpRequest.Headers.Add("Authorization", $"Bearer {req.ApiKey}");
            httpRequest.Content = new StringContent(requestJson, System.Text.Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(httpRequest, req.CancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(req.CancellationToken);
                _logger.LogWarning("OpenAI API returned {StatusCode}: {ErrorContent}", response.StatusCode, errorContent);

                // Handle rate limiting specially
                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    return new SpamCheckResponse
                    {
                        CheckName = CheckName,
                        Result = SpamCheckResultType.Clean, // Fail open during rate limits
                        Details = "OpenAI API rate limited - allowing message",
                        Confidence = 0,
                        Error = new HttpRequestException($"OpenAI API rate limited: {response.StatusCode}")
                    };
                }

                return new SpamCheckResponse
                {
                    CheckName = CheckName,
                    Result = SpamCheckResultType.Clean,
                    Details = $"OpenAI API error: {response.StatusCode}",
                    Confidence = 0,
                    Error = new HttpRequestException($"OpenAI API error: {response.StatusCode}")
                };
            }

            var responseJson = await response.Content.ReadAsStringAsync(req.CancellationToken);
            var openaiResponse = JsonSerializer.Deserialize<OpenAIResponse>(responseJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (openaiResponse?.Choices?.Any() != true)
            {
                _logger.LogWarning("Invalid OpenAI API response for user {UserId}", req.UserId);
                return new SpamCheckResponse
                {
                    CheckName = CheckName,
                    Result = SpamCheckResultType.Clean,
                    Details = "Invalid OpenAI response",
                    Confidence = 0,
                    Error = new InvalidOperationException("Invalid OpenAI response format")
                };
            }

            // Cache the result for 1 hour
            _cache.Set(cacheKey, openaiResponse, TimeSpan.FromHours(1));

            return CreateResponse(openaiResponse, req, fromCache: false);
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("OpenAI check for user {UserId}: Request timed out", req.UserId);
            return new SpamCheckResponse
            {
                CheckName = CheckName,
                Result = SpamCheckResultType.Clean, // Fail open on timeout
                Details = "OpenAI check timed out - allowing message",
                Confidence = 0,
                Error = new TimeoutException("OpenAI API request timed out")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenAI check for user {UserId}: Unexpected error", req.UserId);
            return new SpamCheckResponse
            {
                CheckName = CheckName,
                Result = SpamCheckResultType.Clean, // Fail open on error
                Details = "OpenAI check failed due to error",
                Confidence = 0,
                Error = ex
            };
        }
    }

    /// <summary>
    /// Create enhanced OpenAI API request with history context and JSON response format
    /// Phase 4.5: Uses modular prompt building system
    /// </summary>
    private async Task<OpenAIRequest> CreateOpenAIRequestAsync(OpenAICheckRequest req)
    {
        // Phase 4.5: Use modular prompt builder
        // Custom prompt (if provided) replaces the default rules section only
        var systemPrompt = BuildSystemPrompt(req.VetoMode, req.SystemPrompt);

        // Get message history for context
        var history = await _messageHistoryService.GetRecentMessagesAsync(req.ChatId ?? "unknown", 5, req.CancellationToken);
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

        var userPrompt = req.VetoMode
            ? $"Analyze this message that was flagged by other spam filters. Is it actually spam?\n\n{contextBuilder}\nCurrent message from user {req.UserName} (ID: {req.UserId}):\n\"{req.Message}\"\n\nRespond with JSON: {{\"result\": \"spam\" or \"clean\" or \"review\", \"reason\": \"explanation\", \"confidence\": 0.0-1.0}}"
            : $"Analyze this message for spam content.\n\n{contextBuilder}\nMessage from user {req.UserName} (ID: {req.UserId}):\n\"{req.Message}\"\n\nRespond with JSON: {{\"result\": \"spam\" or \"clean\" or \"review\", \"reason\": \"explanation\", \"confidence\": 0.0-1.0}}";

        return new OpenAIRequest
        {
            Model = req.Model,
            Messages =
            [
                new OpenAIMessage { Role = "system", Content = systemPrompt },
                new OpenAIMessage { Role = "user", Content = userPrompt }
            ],
            MaxTokens = req.MaxTokens,
            Temperature = 0.1,
            TopP = 1.0,
            ResponseFormat = new { type = "json_object" }
        };
    }

    /// <summary>
    /// Phase 4.5: Get technical base prompt (unchangeable by users)
    /// Defines JSON format and result types
    /// </summary>
    private static string GetBaseTechnicalPrompt()
    {
        return """
            You must respond with valid JSON in this exact format:
            {
              "result": "spam" | "clean" | "review",
              "reason": "clear explanation of your decision",
              "confidence": 0.0-1.0
            }

            Result types:
            - "spam": Message is definitely spam/scam/unwanted
            - "clean": Message is legitimate conversation
            - "review": Uncertain - requires human review
            """;
    }

    /// <summary>
    /// Phase 4.5: Get default spam/legitimate content rules
    /// Can be overridden by chat-specific custom prompts
    /// </summary>
    private static string GetDefaultRulesPrompt()
    {
        return """
            SPAM indicators (mark as "spam"):
            - Personal testimonials promoting paid services/individuals ("X transformed my life/trading/income")
            - Direct solicitation or selling of services
            - Get-rich-quick schemes or unrealistic profit promises
            - Requests to contact someone for trading/investment advice
            - Scam signals: "fee-free", "guaranteed profits", "no tricks", success stories
            - Unsolicited financial advice with calls-to-action
            - Adult content, obvious scams, repetitive spam patterns

            LEGITIMATE content (mark as "clean"):
            - Genuine discussion about crypto, trading, AI, or technology topics
            - Educational content, tutorials, or proof-of-concepts being shared
            - News articles, research, or analysis
            - Questions and answers about topics
            - Sharing legitimate tools, resources, or links for discussion
            - Normal conversation about markets, technology, or current events

            Key distinction: Sharing knowledge/discussion = legitimate. Promoting services/testimonials = spam.
            """;
    }

    /// <summary>
    /// Phase 4.5: Get mode-specific guidance (veto vs detection)
    /// </summary>
    private static string GetModeGuidancePrompt(bool vetoMode)
    {
        if (vetoMode)
        {
            return """
                MODE: Spam Verification (Veto)
                Other filters have flagged this message as potential spam. Your job is to verify if it's actually spam or a false positive.

                Return "spam" (confirm spam) if:
                - The message clearly matches spam indicators above
                - Contains personal testimonials, solicitation, or promotional content
                - You agree with the other filters' assessment

                Return "clean" (veto/override) if:
                - The message is educational, informational, or conversational in nature
                - No direct solicitation or testimonial promoting paid services
                - Legitimate sharing of resources, tools, or ideas for group discussion
                - You disagree with the other filters (false positive)

                Return "review" if:
                - You're uncertain whether it's spam or legitimate
                - The message is borderline or context-dependent
                - Human judgment would be more reliable

                Be cautious with vetoes - only override if you're confident it's a false positive.
                """;
        }

        return """
            MODE: Spam Detection
            Analyze this message and determine if it's spam.

            Return "spam" if:
            - Message clearly matches spam indicators above
            - Promotional, solicitation, or scam content

            Return "clean" if:
            - Legitimate conversation or discussion
            - When in doubt, lean toward "clean" to preserve conversation

            Return "review" if:
            - Uncertain or borderline case
            - Requires human judgment

            Be conservative - false positives (blocking legitimate messages) are worse than false negatives.
            """;
    }

    /// <summary>
    /// Phase 4.5: Build complete system prompt from modular components
    /// </summary>
    private static string BuildSystemPrompt(bool vetoMode, string? customRulesPrompt = null)
    {
        var baseTechnical = GetBaseTechnicalPrompt();
        var rules = customRulesPrompt ?? GetDefaultRulesPrompt();
        var modeGuidance = GetModeGuidancePrompt(vetoMode);

        return $"""
            {baseTechnical}

            {rules}

            {modeGuidance}

            Consider the message context, user history, and conversation flow when making your decision.
            Always respond with valid JSON format.
            """;
    }

    /// <summary>
    /// Create spam check response from OpenAI API response with JSON parsing and fallback
    /// </summary>
    private SpamCheckResponse CreateResponse(OpenAIResponse openaiResponse, OpenAICheckRequest req, bool fromCache)
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
                // Parse the result string into enum (type-safe)
                var result = (jsonResponse.Result?.ToLowerInvariant()) switch
                {
                    "spam" => SpamCheckResultType.Spam,
                    "clean" => SpamCheckResultType.Clean,
                    "review" => SpamCheckResultType.Review,
                    _ => SpamCheckResultType.Clean // Default fail-open
                };

                var confidence = (int)Math.Round((jsonResponse.Confidence ?? 0.8) * 100);

                // Build details message based on result
                var details = result switch
                {
                    SpamCheckResultType.Spam => req.VetoMode
                        ? $"OpenAI confirmed spam: {jsonResponse.Reason}"
                        : $"OpenAI detected spam: {jsonResponse.Reason}",
                    SpamCheckResultType.Clean => req.VetoMode
                        ? $"OpenAI vetoed spam: {jsonResponse.Reason}"
                        : $"OpenAI found no spam: {jsonResponse.Reason}",
                    SpamCheckResultType.Review => $"OpenAI flagged for review: {jsonResponse.Reason}",
                    _ => $"OpenAI result: {jsonResponse.Reason}"
                };

                if (fromCache)
                {
                    details += " (cached)";
                }

                _logger.LogDebug("OpenAI check completed: Result={Result}, Confidence={Confidence}, Reason={Reason}, FromCache={FromCache}",
                    result, confidence, jsonResponse.Reason, fromCache);

                return new SpamCheckResponse
                {
                    CheckName = CheckName,
                    Result = result,
                    Details = details,
                    Confidence = confidence
                };
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse OpenAI JSON response: {Content}", content);
        }

        // Fallback to legacy parsing if JSON fails
        return CreateLegacyResponse(content, req, fromCache);
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
            Result = SpamCheckResultType.Clean, // Fail open
            Details = details,
            Confidence = 0
        };
    }

    /// <summary>
    /// Legacy response parsing for non-JSON responses
    /// </summary>
    private SpamCheckResponse CreateLegacyResponse(string content, OpenAICheckRequest req, bool fromCache)
    {
        var upperContent = content.ToUpperInvariant();
        var isSpam = upperContent.Contains("SPAM") && !upperContent.Contains("NOT_SPAM");
        var result = isSpam ? SpamCheckResultType.Spam : SpamCheckResultType.Clean;
        var confidence = isSpam ? 75 : 0; // Lower confidence for legacy parsing

        var details = req.VetoMode
            ? (isSpam ? "OpenAI confirmed spam (legacy)" : "OpenAI vetoed spam (legacy)")
            : (isSpam ? "OpenAI detected spam (legacy)" : "OpenAI found no spam (legacy)");

        if (fromCache)
        {
            details += " (cached)";
        }

        _logger.LogDebug("OpenAI legacy parsing: Result={Result}, Confidence={Confidence}, Content={Content}",
            result, confidence, content.Length > 100 ? content[..100] + "..." : content);

        return new SpamCheckResponse
        {
            CheckName = CheckName,
            Result = result,
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
    public string? Result { get; init; } // "spam", "clean", or "review"
    public string? Reason { get; init; }
    public double? Confidence { get; init; }
}