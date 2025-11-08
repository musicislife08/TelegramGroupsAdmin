using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.ContentDetection.Abstractions;
using TelegramGroupsAdmin.ContentDetection.Constants;
using TelegramGroupsAdmin.ContentDetection.Helpers;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Services;

namespace TelegramGroupsAdmin.ContentDetection.Checks;

/// <summary>
/// Enhanced OpenAI spam check with history context, JSON responses, and fallback
/// Improved veto system based on tg-spam with additional context and reliability
/// </summary>
public class OpenAIContentCheck(
    ILogger<OpenAIContentCheck> logger,
    IHttpClientFactory httpClientFactory,
    IMemoryCache cache,
    IMessageHistoryService messageHistoryService) : IContentCheck
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
    /// Execute OpenAI spam check
    /// </summary>
    public async ValueTask<ContentCheckResponse> CheckAsync(ContentCheckRequestBase request)
    {
        var req = (OpenAICheckRequest)request;

        try
        {
            // Skip short messages unless specifically configured to check them
            if (!req.CheckShortMessages && req.Message.Length < req.MinMessageLength)
            {
                return new ContentCheckResponse
                {
                    CheckName = CheckName,
                    Result = CheckResultType.Clean,
                    Details = $"Message too short (< {req.MinMessageLength} chars)",
                    Confidence = 0
                };
            }

            // In veto mode, only run if other checks flagged as spam
            if (req.VetoMode && !req.HasSpamFlags)
            {
                return new ContentCheckResponse
                {
                    CheckName = CheckName,
                    Result = CheckResultType.Clean,
                    Details = "Veto mode: no spam flags from other checks",
                    Confidence = 0
                };
            }

            // Check cache first
            var cacheKey = $"openai_check_{GetMessageHash(req.Message)}";
            if (cache.TryGetValue(cacheKey, out OpenAIResponse? cachedResponse) && cachedResponse != null)
            {
                logger.LogDebug("OpenAI check for user {UserId}: Using cached result", req.UserId);
                return OpenAIResponseParser.ParseResponse(cachedResponse, req, fromCache: true, logger);
            }

            // Check API key
            if (string.IsNullOrEmpty(req.ApiKey))
            {
                logger.LogWarning("OpenAI API key not configured");
                return new ContentCheckResponse
                {
                    CheckName = CheckName,
                    Result = CheckResultType.Clean,
                    Details = "OpenAI API key not configured",
                    Confidence = 0,
                    Error = new InvalidOperationException("OpenAI API key not configured")
                };
            }

            // Get message history for context
            var history = await messageHistoryService.GetRecentMessagesAsync(req.ChatId, 5, req.CancellationToken);

            // Prepare the API request with history context using static prompt builder
            var apiRequest = OpenAIPromptBuilder.CreateRequest(req, history);
            var requestJson = JsonSerializer.Serialize(apiRequest, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
            });

            logger.LogDebug("OpenAI check for user {UserId}: Calling API with message length {MessageLength}",
                req.UserId, req.Message.Length);

            // Make API call using named "OpenAI" HttpClient (configured in ServiceCollectionExtensions)
            var httpClient = _httpClientFactory.CreateClient("OpenAI");
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "chat/completions");
            httpRequest.Content = new StringContent(requestJson, System.Text.Encoding.UTF8, "application/json");

            using var response = await httpClient.SendAsync(httpRequest, req.CancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(req.CancellationToken);
                logger.LogWarning("OpenAI API returned {StatusCode}: {ErrorContent}", response.StatusCode, errorContent);

                // Handle rate limiting specially
                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    return new ContentCheckResponse
                    {
                        CheckName = CheckName,
                        Result = CheckResultType.Clean, // Fail open during rate limits
                        Details = "OpenAI API rate limited - allowing message",
                        Confidence = 0,
                        Error = new HttpRequestException($"OpenAI API rate limited: {response.StatusCode}")
                    };
                }

                return new ContentCheckResponse
                {
                    CheckName = CheckName,
                    Result = CheckResultType.Clean,
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
                logger.LogWarning("Invalid OpenAI API response for user {UserId}", req.UserId);
                return new ContentCheckResponse
                {
                    CheckName = CheckName,
                    Result = CheckResultType.Clean,
                    Details = "Invalid OpenAI response",
                    Confidence = 0,
                    Error = new InvalidOperationException("Invalid OpenAI response format")
                };
            }

            // Cache the result for 1 hour
            cache.Set(cacheKey, openaiResponse, TimeSpan.FromHours(1));

            return OpenAIResponseParser.ParseResponse(openaiResponse, req, fromCache: false, logger);
        }
        catch (TaskCanceledException)
        {
            logger.LogWarning("OpenAI check for user {UserId}: Request timed out", req.UserId);
            return new ContentCheckResponse
            {
                CheckName = CheckName,
                Result = CheckResultType.Clean, // Fail open on timeout
                Details = "OpenAI check timed out - allowing message",
                Confidence = 0,
                Error = new TimeoutException("OpenAI API request timed out")
            };
        }
        catch (Exception ex)
        {
            return ContentCheckHelpers.CreateFailureResponse(CheckName, ex, logger, req.UserId);
        }
    }


    /// <summary>
    /// Generate a hash for message caching
    /// </summary>
    private static string GetMessageHash(string message)
    {
        return message.Length.ToString() + "_" + Math.Abs(message.GetHashCode()).ToString();
    }
}