using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.ContentDetection.Abstractions;
using TelegramGroupsAdmin.ContentDetection.Constants;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Services;

namespace TelegramGroupsAdmin.ContentDetection.Checks;

/// <summary>
/// V2 OpenAI spam check with proper scoring (0.0-5.0 points)
/// Can operate in two modes:
/// 1. Regular mode: Acts like any other check, contributes score
/// 2. Veto mode: Engine-level override (handled by ContentDetectionEngineV2)
/// </summary>
public class OpenAIContentCheckV2(
    ILogger<OpenAIContentCheckV2> logger,
    IHttpClientFactory httpClientFactory,
    IMemoryCache cache,
    IMessageHistoryService messageHistoryService) : IContentCheckV2
{
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;

    public CheckName CheckName => CheckName.OpenAI;

    /// <summary>
    /// Check if OpenAI check should be executed
    /// </summary>
    public bool ShouldExecute(ContentCheckRequest request)
    {
        // Skip empty messages
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return false;
        }

        // PERF-3 Option B: Skip expensive OpenAI API calls for trusted/admin users
        // OpenAI is not a critical check - it's expensive and should skip for trusted users
        if (request.IsUserTrusted || request.IsUserAdmin)
        {
            logger.LogDebug(
                "Skipping OpenAI check for user {UserId}: User is {UserType}",
                request.UserId,
                request.IsUserTrusted ? "trusted" : "admin");
            return false;
        }

        // Check if enabled is done in CheckAsync since we need to load config from DB
        return true;
    }

    /// <summary>
    /// Execute OpenAI spam check and return V2 score
    /// </summary>
    public async ValueTask<ContentCheckResponseV2> CheckAsync(ContentCheckRequestBase request)
    {
        var req = (OpenAICheckRequest)request;

        try
        {
            // Skip short messages unless specifically configured to check them
            if (!req.CheckShortMessages && req.Message.Length < req.MinMessageLength)
            {
                return new ContentCheckResponseV2
                {
                    CheckName = CheckName,
                    Score = 0.0,
                    Abstained = true,
                    Details = $"Message too short (< {req.MinMessageLength} chars)"
                };
            }

            // In veto mode, only run if other checks flagged as spam
            // Note: This is typically handled by engine, but check here too as fallback
            if (req.VetoMode && !req.HasSpamFlags)
            {
                return new ContentCheckResponseV2
                {
                    CheckName = CheckName,
                    Score = 0.0,
                    Abstained = true,
                    Details = "Veto mode: no spam flags from other checks"
                };
            }

            // Check cache first
            var cacheKey = $"openai_check_{GetMessageHash(req.Message)}";
            if (cache.TryGetValue(cacheKey, out OpenAIResponse? cachedResponse) && cachedResponse != null)
            {
                logger.LogDebug("OpenAI V2 check for user {UserId}: Using cached result", req.UserId);
                return ParseOpenAIResponse(cachedResponse, fromCache: true);
            }

            // Note: API key is injected via ApiKeyDelegatingHandler on the named "OpenAI" HttpClient

            // Get message history for context (count from config)
            var history = await messageHistoryService.GetRecentMessagesAsync(req.ChatId, req.MessageHistoryCount, req.CancellationToken);

            // Prepare the API request with history context using static prompt builder
            var apiRequest = OpenAIPromptBuilder.CreateRequest(req, history);
            var requestJson = JsonSerializer.Serialize(apiRequest, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
            });

            logger.LogDebug("OpenAI V2 check for user {UserId}: Calling API with message length {MessageLength}",
                req.UserId, req.Message.Length);

            // Make API call using named "OpenAI" HttpClient (configured in ServiceCollectionExtensions)
            var httpClient = _httpClientFactory.CreateClient("OpenAI");
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "chat/completions");
            httpRequest.Content = new StringContent(requestJson, System.Text.Encoding.UTF8, "application/json");

            using var response = await httpClient.SendAsync(httpRequest, req.CancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(req.CancellationToken);
                logger.LogWarning("OpenAI API returned {StatusCode}: {ErrorContent}, abstaining", response.StatusCode, errorContent);

                // Handle rate limiting specially
                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    return new ContentCheckResponseV2
                    {
                        CheckName = CheckName,
                        Score = 0.0,
                        Abstained = true,
                        Details = "OpenAI API rate limited - abstaining"
                    };
                }

                return new ContentCheckResponseV2
                {
                    CheckName = CheckName,
                    Score = 0.0,
                    Abstained = true,
                    Details = $"OpenAI API error: {response.StatusCode}"
                };
            }

            var responseJson = await response.Content.ReadAsStringAsync(req.CancellationToken);
            var openaiResponse = JsonSerializer.Deserialize<OpenAIResponse>(responseJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (openaiResponse?.Choices?.Any() != true)
            {
                logger.LogWarning("Invalid OpenAI API response for user {UserId}, abstaining", req.UserId);
                return new ContentCheckResponseV2
                {
                    CheckName = CheckName,
                    Score = 0.0,
                    Abstained = true,
                    Details = "Invalid OpenAI response"
                };
            }

            // Cache the result for 1 hour
            cache.Set(cacheKey, openaiResponse, TimeSpan.FromHours(1));

            return ParseOpenAIResponse(openaiResponse, fromCache: false);
        }
        catch (TaskCanceledException)
        {
            logger.LogWarning("OpenAI check for user {UserId}: Request timed out, abstaining", req.UserId);
            return new ContentCheckResponseV2
            {
                CheckName = CheckName,
                Score = 0.0,
                Abstained = true,
                Details = "OpenAI check timed out - abstaining"
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in OpenAI V2 check for user {UserId}, abstaining", req.UserId);
            return new ContentCheckResponseV2
            {
                CheckName = CheckName,
                Score = 0.0,
                Abstained = true,
                Details = $"Error: {ex.Message}",
                Error = ex
            };
        }
    }

    /// <summary>
    /// Parse OpenAI response and return V2 score
    /// </summary>
    private ContentCheckResponseV2 ParseOpenAIResponse(OpenAIResponse response, bool fromCache)
    {
        var content = response.Choices?.FirstOrDefault()?.Message?.Content?.Trim();

        if (string.IsNullOrWhiteSpace(content))
        {
            logger.LogWarning("Empty content from OpenAI, abstaining");
            return new ContentCheckResponseV2
            {
                CheckName = CheckName,
                Score = 0.0,
                Abstained = true,
                Details = "Empty OpenAI response"
            };
        }

        try
        {
            // Parse JSON response
            var jsonResponse = JsonSerializer.Deserialize<OpenAIJsonResponse>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (jsonResponse == null)
            {
                logger.LogWarning("Failed to deserialize OpenAI JSON response: {Content}", content);
                return new ContentCheckResponseV2
                {
                    CheckName = CheckName,
                    Score = 0.0,
                    Abstained = true,
                    Details = "Invalid JSON response"
                };
            }

            // Parse result: "spam", "clean", or "review"
            var isSpam = jsonResponse.Result?.ToLowerInvariant() == "spam";
            var isReview = jsonResponse.Result?.ToLowerInvariant() == "review";

            // Clean result = abstain (0 points)
            if (!isSpam && !isReview)
            {
                var details = $"OpenAI: Clean - {jsonResponse.Reason}";
                if (fromCache) details += " (cached)";

                return new ContentCheckResponseV2
                {
                    CheckName = CheckName,
                    Score = 0.0,
                    Abstained = true,
                    Details = details
                };
            }

            // Map confidence (0.0-1.0) to V2 score (0.0-5.0)
            var confidence = jsonResponse.Confidence ?? SpamDetectionConstants.DefaultOpenAIConfidence;
            var score = confidence * 5.0;

            // Review = medium score (capped at review threshold)
            if (isReview)
            {
                score = Math.Min(score, SpamDetectionConstants.ReviewThreshold); // Cap review at review threshold
            }

            var spamDetails = $"OpenAI: {(isReview ? "Review" : "Spam")} - {jsonResponse.Reason}";
            if (fromCache) spamDetails += " (cached)";

            return new ContentCheckResponseV2
            {
                CheckName = CheckName,
                Score = score,
                Abstained = false,
                Details = spamDetails
            };
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to parse OpenAI JSON response: {Content}", content);
            return new ContentCheckResponseV2
            {
                CheckName = CheckName,
                Score = 0.0,
                Abstained = true,
                Details = "Failed to parse OpenAI response",
                Error = ex
            };
        }
    }

    /// <summary>
    /// OpenAI JSON response structure
    /// </summary>
    private record OpenAIJsonResponse
    {
        public string? Result { get; init; } // "spam", "clean", or "review"
        public string? Reason { get; init; }
        public double? Confidence { get; init; } // 0.0-1.0
    }

    /// <summary>
    /// Generate a hash for message caching
    /// </summary>
    private static string GetMessageHash(string message)
    {
        return message.Length.ToString() + "_" + Math.Abs(message.GetHashCode()).ToString();
    }
}
