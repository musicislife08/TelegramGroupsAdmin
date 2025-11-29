using System.Text.Json;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.ContentDetection.Constants;
using TelegramGroupsAdmin.ContentDetection.Models;

namespace TelegramGroupsAdmin.ContentDetection.Services;

/// <summary>
/// Static utility class for parsing OpenAI API responses (JSON-only)
/// </summary>
public static class OpenAIResponseParser
{
    /// <summary>
    /// Create spam check response from OpenAI API response
    /// </summary>
    public static ContentCheckResponse ParseResponse(
        OpenAIResponse openaiResponse,
        OpenAICheckRequest req,
        bool fromCache,
        ILogger logger)
    {
        var choice = openaiResponse.Choices?.FirstOrDefault();
        var content = choice?.Message?.Content?.Trim();

        if (string.IsNullOrEmpty(content))
        {
            logger.LogWarning("OpenAI returned empty content");
            return CreateFailureResponse("Empty OpenAI response", fromCache);
        }

        // Parse JSON response
        try
        {
            var jsonResponse = JsonSerializer.Deserialize<OpenAIJsonResponse>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (jsonResponse == null)
            {
                logger.LogWarning("Failed to deserialize OpenAI JSON response: {Content}", content);
                return CreateFailureResponse("Invalid JSON response", fromCache);
            }

            // Parse the result string into enum (type-safe)
            var result = (jsonResponse.Result?.ToLowerInvariant()) switch
            {
                "spam" => CheckResultType.Spam,
                "clean" => CheckResultType.Clean,
                "review" => CheckResultType.Review,
                _ => CheckResultType.Clean // Default fail-open for unknown results
            };

            var confidence = (int)Math.Round((jsonResponse.Confidence ?? ContentDetectionConstants.DefaultOpenAIConfidence) * 100);

            // Build details message based on result
            var details = result switch
            {
                CheckResultType.Spam => req.VetoMode
                    ? $"OpenAI confirmed spam: {jsonResponse.Reason}"
                    : $"OpenAI detected spam: {jsonResponse.Reason}",
                CheckResultType.Clean => req.VetoMode
                    ? $"OpenAI vetoed spam: {jsonResponse.Reason}"
                    : $"OpenAI found no spam: {jsonResponse.Reason}",
                CheckResultType.Review => $"OpenAI flagged for review: {jsonResponse.Reason}",
                _ => $"OpenAI result: {jsonResponse.Reason}"
            };

            if (fromCache)
            {
                details += " (cached)";
            }

            logger.LogDebug("OpenAI check completed: Result={Result}, Confidence={Confidence}, Reason={Reason}, FromCache={FromCache}",
                result, confidence, jsonResponse.Reason, fromCache);

            return new ContentCheckResponse
            {
                CheckName = CheckName.OpenAI,
                Result = result,
                Details = details,
                Confidence = confidence
            };
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to parse OpenAI JSON response: {Content}", content);
            return CreateFailureResponse($"JSON parsing error: {ex.Message}", fromCache);
        }
    }

    /// <summary>
    /// Create failure response when OpenAI parsing fails (fail-open)
    /// </summary>
    private static ContentCheckResponse CreateFailureResponse(string reason, bool fromCache)
    {
        var details = $"OpenAI error: {reason} - allowing message";
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
/// OpenAI API response structure
/// </summary>
public record OpenAIResponse
{
    public OpenAIChoice[]? Choices { get; init; }
    public OpenAIUsage? Usage { get; init; }
}

/// <summary>
/// OpenAI choice structure
/// </summary>
public record OpenAIChoice
{
    public OpenAIMessage? Message { get; init; }
    public string? FinishReason { get; init; }
}

/// <summary>
/// OpenAI usage structure
/// </summary>
public record OpenAIUsage
{
    public int PromptTokens { get; init; }
    public int CompletionTokens { get; init; }
    public int TotalTokens { get; init; }
}

/// <summary>
/// OpenAI JSON response structure for enhanced spam detection
/// </summary>
public record OpenAIJsonResponse
{
    public string? Result { get; init; } // "spam", "clean", or "review"
    public string? Reason { get; init; }
    public double? Confidence { get; init; }
}
