using System.Text.Json;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.ContentDetection.Constants;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.Core.Services.AI;

namespace TelegramGroupsAdmin.ContentDetection.Services;

/// <summary>
/// Utility class for parsing AI chat completion responses for spam veto.
/// Provider-agnostic - works with ChatCompletionResult from any AI provider via IChatService.
/// </summary>
public static class AIResponseParser
{
    /// <summary>
    /// Create spam check response from AI chat completion result.
    /// AI always runs as veto to verify spam detected by other checks.
    /// </summary>
    public static ContentCheckResponse ParseResponse(
        ChatCompletionResult result,
        AIVetoCheckRequest req,
        bool fromCache,
        ILogger logger)
    {
        var content = result.Content?.Trim();

        if (string.IsNullOrEmpty(content))
        {
            logger.LogWarning("AI returned empty content");
            return CreateFailureResponse("Empty AI response", fromCache);
        }

        // Parse JSON response (expected format from our prompt)
        try
        {
            var jsonResponse = JsonSerializer.Deserialize<AIJsonResponse>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (jsonResponse == null)
            {
                logger.LogWarning("Failed to deserialize AI JSON response: {Content}", content);
                return CreateFailureResponse("Invalid JSON response", fromCache);
            }

            // Parse the result string into enum (type-safe)
            var checkResult = (jsonResponse.Result?.ToLowerInvariant()) switch
            {
                "spam" => CheckResultType.Spam,
                "clean" => CheckResultType.Clean,
                "review" => CheckResultType.Review,
                _ => CheckResultType.Clean // Default fail-open for unknown results
            };

            var confidence = (int)Math.Round((jsonResponse.Confidence ?? ContentDetectionConstants.DefaultOpenAIConfidence) * 100);

            // Build details message - AI always runs as veto
            var details = checkResult switch
            {
                CheckResultType.Spam => $"AI confirmed spam: {jsonResponse.Reason}",
                CheckResultType.Clean => $"AI vetoed spam: {jsonResponse.Reason}",
                CheckResultType.Review => $"AI flagged for review: {jsonResponse.Reason}",
                _ => $"AI result: {jsonResponse.Reason}"
            };

            if (fromCache)
            {
                details += " (cached)";
            }

            logger.LogDebug("AI veto check completed: Result={Result}, Confidence={Confidence}, Reason={Reason}, FromCache={FromCache}",
                checkResult, confidence, jsonResponse.Reason, fromCache);

            return new ContentCheckResponse
            {
                CheckName = CheckName.OpenAI,
                Result = checkResult,
                Details = details,
                Confidence = confidence
            };
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to parse AI JSON response: {Content}", content);
            return CreateFailureResponse($"JSON parsing error: {ex.Message}", fromCache);
        }
    }

    /// <summary>
    /// Create failure response when AI parsing fails (fail-open)
    /// </summary>
    private static ContentCheckResponse CreateFailureResponse(string reason, bool fromCache)
    {
        var details = $"AI error: {reason} - allowing message";
        if (fromCache)
        {
            details += " (cached)";
        }

        return new ContentCheckResponse
        {
            CheckName = CheckName.OpenAI,
            Result = CheckResultType.Clean, // Fail open
            Details = details,
            Confidence = 0
        };
    }
}

/// <summary>
/// Expected JSON response structure from AI for spam detection.
/// This is the format we request in our prompts.
/// </summary>
public record AIJsonResponse
{
    public string? Result { get; init; } // "spam", "clean", or "review"
    public string? Reason { get; init; }
    public double? Confidence { get; init; }
}
