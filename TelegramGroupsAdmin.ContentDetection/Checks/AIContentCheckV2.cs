using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.ContentDetection.Abstractions;
using TelegramGroupsAdmin.ContentDetection.Constants;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Services;
using TelegramGroupsAdmin.Core.Extensions;
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
    HybridCache cache,
    IMessageContextProvider messageContextProvider) : IContentCheckV2
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
                "Skipping AI check for {User}: User is {UserType}",
                request.User.ToLogDebug(),
                request.IsUserTrusted ? "trusted" : "admin");
            return false;
        }

        // Check if enabled is done in CheckAsync since we need to load config from DB
        return true;
    }

    private static readonly HybridCacheEntryOptions AICacheOptions = new()
    {
        Expiration = TimeSpan.FromHours(AIConstants.CacheDurationHours)
    };

    /// <summary>
    /// Execute AI spam check and return V2 score
    /// </summary>
    public async ValueTask<ContentCheckResponseV2> CheckAsync(ContentCheckRequestBase request)
    {
        var startTimestamp = Stopwatch.GetTimestamp();
        var req = (AIVetoCheckRequest)request;

        try
        {
            // Combine caption, OCR text, and Vision analysis for analysis
            var effectiveText = req.Message;
            if (!string.IsNullOrWhiteSpace(req.OcrExtractedText))
            {
                effectiveText = string.IsNullOrWhiteSpace(effectiveText)
                    ? req.OcrExtractedText
                    : $"{effectiveText}\n\n<image-text>\n{req.OcrExtractedText}\n</image-text>";
            }

            // Add Vision analysis text (raw reason/patterns from image spam detection)
            if (!string.IsNullOrWhiteSpace(req.VisionAnalysisText))
            {
                effectiveText = string.IsNullOrWhiteSpace(effectiveText)
                    ? req.VisionAnalysisText
                    : $"{effectiveText}\n\n<image-analysis>\n{req.VisionAnalysisText}\n</image-analysis>";
            }

            // Skip short messages unless specifically configured to check them
            // Check combined length (not just caption) - handles OCR-only cases
            if (!req.CheckShortMessages && effectiveText.Length < req.MinMessageLength)
            {
                return new ContentCheckResponseV2
                {
                    CheckName = CheckName,
                    Score = 0.0,
                    Abstained = true,
                    Details = $"Combined text too short (< {req.MinMessageLength} chars)",
                    ProcessingTimeMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds
                };
            }

            // AI veto only runs when other checks flagged as spam
            // Note: This is typically handled by engine, but check here too as fallback
            if (!req.HasSpamFlags)
            {
                return new ContentCheckResponseV2
                {
                    CheckName = CheckName,
                    Score = 0.0,
                    Abstained = true,
                    Details = "No spam flags to verify",
                    ProcessingTimeMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds
                };
            }

            // Use GetOrCreateAsync for cache-aside with stampede protection
            // Cache by content hash to avoid reprocessing identical messages
            var cacheKey = $"ai_check_{GetMessageHash(effectiveText)}";
            var (result, fromCache) = await GetOrFetchAIResultAsync(cacheKey, req);

            return ParseAIResponse(result, fromCache, startTimestamp);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("AI service"))
        {
            // AI service returned null - don't cache, abstain
            logger.LogWarning("AI API returned null result for {User}, abstaining", req.User.ToLogDebug());
            return new ContentCheckResponseV2
            {
                CheckName = CheckName,
                Score = 0.0,
                Abstained = true,
                Details = "AI returned no response",
                ProcessingTimeMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds
            };
        }
        catch (TaskCanceledException)
        {
            logger.LogWarning("AI check for {User}: Request timed out, abstaining", req.User.ToLogDebug());
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
            logger.LogError(ex, "Error in AI V2 check for {User}, abstaining", req.User.ToLogDebug());
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
    /// Get cached AI result or fetch from API with stampede protection.
    /// Returns tuple of (result, fromCache) to preserve cache hit logging.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when AI service returns null. Prevents HybridCache from caching transient errors.
    /// </exception>
    private async Task<(ChatCompletionResult Result, bool FromCache)> GetOrFetchAIResultAsync(
        string cacheKey, AIVetoCheckRequest req)
    {
        // Track whether this was a cache hit for logging
        var wasCacheHit = true;

        var result = await cache.GetOrCreateAsync(
            cacheKey,
            async ct =>
            {
                wasCacheHit = false; // Factory called = cache miss

                // Get message history for context (count from config)
                var history = await messageContextProvider.GetRecentMessagesAsync(req.Chat.Id, req.MessageHistoryCount, ct);

                // Build prompts using the prompt builder
                var prompts = AIPromptBuilder.CreatePrompts(req, history);

                logger.LogDebug("AI V2 check for {User}: Calling AI service (caption: {CaptionLength}, OCR: {OcrLength}, Vision: {VisionLength})",
                    req.User.ToLogDebug(), req.Message?.Length ?? 0, req.OcrExtractedText?.Length ?? 0, req.VisionAnalysisText?.Length ?? 0);

                // Make AI call using the chat service
                var aiResult = await chatService.GetCompletionAsync(
                    AIFeatureType.SpamDetection,
                    prompts.SystemPrompt,
                    prompts.UserPrompt,
                    new ChatCompletionOptions
                    {
                        MaxTokens = req.MaxTokens,
                        JsonMode = true
                    },
                    ct);

                // Throw if null to prevent caching transient failures
                return aiResult ?? throw new InvalidOperationException("AI service returned null");
            },
            AICacheOptions,
            cancellationToken: req.CancellationToken);

        if (wasCacheHit)
        {
            logger.LogDebug("AI V2 check for {User}: Using cached result", req.User.ToLogDebug());
        }

        return (result, wasCacheHit);
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
            var score = confidence * AIConstants.ConfidenceToScoreMultiplier;

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
