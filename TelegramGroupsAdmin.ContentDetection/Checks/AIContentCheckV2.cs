using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.ContentDetection.Abstractions;
using TelegramGroupsAdmin.ContentDetection.Constants;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Services;
using TelegramGroupsAdmin.Core.Services.AI;

namespace TelegramGroupsAdmin.ContentDetection.Checks;

/// <summary>
/// V2 AI spam check with proper scoring (0.0-5.0 points).
/// Provider-agnostic - uses IChatService for multi-provider AI support.
/// Can operate in two modes:
/// 1. Regular mode: Acts like any other check, contributes score
/// 2. Veto mode: Engine-level override (handled by ContentDetectionEngineV2)
/// </summary>
public class AIContentCheckV2(
    ILogger<AIContentCheckV2> logger,
    IChatService chatService,
    IMemoryCache cache,
    IMessageHistoryService messageHistoryService) : IContentCheckV2
{
    public CheckName CheckName => CheckName.OpenAI;

    /// <summary>
    /// Check if AI check should be executed
    /// </summary>
    public bool ShouldExecute(ContentCheckRequest request)
    {
        // Skip empty messages
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return false;
        }

        // PERF-3 Option B: Skip expensive AI API calls for trusted/admin users
        // AI is not a critical check - it's expensive and should skip for trusted users
        if (request.IsUserTrusted || request.IsUserAdmin)
        {
            logger.LogDebug(
                "Skipping AI check for user {UserId}: User is {UserType}",
                request.UserId,
                request.IsUserTrusted ? "trusted" : "admin");
            return false;
        }

        // Check if enabled is done in CheckAsync since we need to load config from DB
        return true;
    }

    /// <summary>
    /// Execute AI spam check and return V2 score
    /// </summary>
    public async ValueTask<ContentCheckResponseV2> CheckAsync(ContentCheckRequestBase request)
    {
        var startTimestamp = Stopwatch.GetTimestamp();
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
                    Details = $"Message too short (< {req.MinMessageLength} chars)",
                    ProcessingTimeMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds
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
                    Details = "Veto mode: no spam flags from other checks",
                    ProcessingTimeMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds
                };
            }

            // Check cache first (cache by content hash to avoid reprocessing identical messages)
            var cacheKey = $"ai_check_{GetMessageHash(req.Message)}";
            if (cache.TryGetValue(cacheKey, out ChatCompletionResult? cachedResult) && cachedResult != null)
            {
                logger.LogDebug("AI V2 check for user {UserId}: Using cached result", req.UserId);
                return ParseAIResponse(cachedResult, fromCache: true, startTimestamp);
            }

            // Get message history for context (count from config)
            var history = await messageHistoryService.GetRecentMessagesAsync(req.ChatId, req.MessageHistoryCount, req.CancellationToken);

            // Build prompts using the prompt builder
            var prompts = AIPromptBuilder.CreatePrompts(req, history);

            logger.LogDebug("AI V2 check for user {UserId}: Calling AI service with message length {MessageLength}",
                req.UserId, req.Message.Length);

            // Make AI call using the chat service
            var result = await chatService.GetCompletionAsync(
                AIFeatureType.SpamDetection,
                prompts.SystemPrompt,
                prompts.UserPrompt,
                new ChatCompletionOptions
                {
                    MaxTokens = req.MaxTokens,
                    Temperature = 0.1,
                    JsonMode = true // Request JSON format response
                },
                req.CancellationToken);

            if (result == null)
            {
                logger.LogWarning("AI API returned null result for user {UserId}, abstaining", req.UserId);
                return new ContentCheckResponseV2
                {
                    CheckName = CheckName,
                    Score = 0.0,
                    Abstained = true,
                    Details = "AI returned no response",
                    ProcessingTimeMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds
                };
            }

            // Cache the result for 1 hour
            cache.Set(cacheKey, result, TimeSpan.FromHours(1));

            return ParseAIResponse(result, fromCache: false, startTimestamp);
        }
        catch (TaskCanceledException)
        {
            logger.LogWarning("AI check for user {UserId}: Request timed out, abstaining", req.UserId);
            return new ContentCheckResponseV2
            {
                CheckName = CheckName,
                Score = 0.0,
                Abstained = true,
                Details = "AI check timed out - abstaining",
                ProcessingTimeMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in AI V2 check for user {UserId}, abstaining", req.UserId);
            return new ContentCheckResponseV2
            {
                CheckName = CheckName,
                Score = 0.0,
                Abstained = true,
                Details = $"Error: {ex.Message}",
                Error = ex,
                ProcessingTimeMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds
            };
        }
    }

    /// <summary>
    /// Parse AI response and return V2 score
    /// </summary>
    private ContentCheckResponseV2 ParseAIResponse(ChatCompletionResult response, bool fromCache, long startTimestamp)
    {
        var content = response.Content?.Trim();

        if (string.IsNullOrWhiteSpace(content))
        {
            logger.LogWarning("Empty content from AI, abstaining");
            return new ContentCheckResponseV2
            {
                CheckName = CheckName,
                Score = 0.0,
                Abstained = true,
                Details = "Empty AI response",
                ProcessingTimeMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds
            };
        }

        try
        {
            // Parse JSON response (format defined in our prompt)
            var jsonResponse = JsonSerializer.Deserialize<AIJsonResponse>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (jsonResponse == null)
            {
                logger.LogWarning("Failed to deserialize AI JSON response: {Content}", content);
                return new ContentCheckResponseV2
                {
                    CheckName = CheckName,
                    Score = 0.0,
                    Abstained = true,
                    Details = "Invalid JSON response",
                    ProcessingTimeMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds
                };
            }

            // Parse result: "spam", "clean", or "review"
            var isSpam = jsonResponse.Result?.ToLowerInvariant() == "spam";
            var isReview = jsonResponse.Result?.ToLowerInvariant() == "review";
            var isClean = jsonResponse.Result?.ToLowerInvariant() == "clean";

            // Clean result = definitive verdict (0 points, veto spam detection)
            if (isClean)
            {
                var details = $"AI: Clean - {jsonResponse.Reason}";
                if (fromCache) details += " (cached)";

                return new ContentCheckResponseV2
                {
                    CheckName = CheckName,
                    Score = 0.0,
                    Abstained = false, // Clean is a verdict, not an abstention - triggers veto in engine
                    Details = details,
                    ProcessingTimeMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds
                };
            }

            // Unknown/unparseable result - abstain
            if (!isSpam && !isReview)
            {
                logger.LogWarning("AI returned unknown result: {Result}", jsonResponse.Result);
                return new ContentCheckResponseV2
                {
                    CheckName = CheckName,
                    Score = 0.0,
                    Abstained = true, // Unknown result - can't interpret, defer to pipeline
                    Details = $"Unknown AI result: {jsonResponse.Result}",
                    ProcessingTimeMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds
                };
            }

            // Map confidence (0.0-1.0) to V2 score (0.0-5.0)
            var confidence = jsonResponse.Confidence ?? ContentDetectionConstants.DefaultOpenAIConfidence;
            var score = confidence * 5.0;

            // Review = medium score (capped at review threshold)
            if (isReview)
            {
                score = Math.Min(score, ContentDetectionConstants.ReviewThreshold); // Cap review at review threshold
            }

            var spamDetails = $"AI: {(isReview ? "Review" : "Spam")} - {jsonResponse.Reason}";
            if (fromCache) spamDetails += " (cached)";

            return new ContentCheckResponseV2
            {
                CheckName = CheckName,
                Score = score,
                Abstained = false,
                Details = spamDetails,
                ProcessingTimeMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds
            };
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to parse AI JSON response: {Content}", content);
            return new ContentCheckResponseV2
            {
                CheckName = CheckName,
                Score = 0.0,
                Abstained = true,
                Details = "Failed to parse AI response",
                Error = ex,
                ProcessingTimeMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds
            };
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
