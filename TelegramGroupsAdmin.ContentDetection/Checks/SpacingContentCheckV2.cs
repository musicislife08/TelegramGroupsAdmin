using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.ContentDetection.Abstractions;
using TelegramGroupsAdmin.ContentDetection.Constants;
using TelegramGroupsAdmin.ContentDetection.Models;

namespace TelegramGroupsAdmin.ContentDetection.Checks;

/// <summary>
/// V2 spacing check with proper abstention
/// Scoring: 0.8 points when patterns found (research: formatting anomaly = 0.8)
/// </summary>
public class SpacingContentCheckV2(ILogger<SpacingContentCheckV2> logger) : IContentCheckV2
{
    public CheckName CheckName => CheckName.Spacing;

    public bool ShouldExecute(ContentCheckRequest request)
    {
        // Skip empty messages
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return false;
        }

        // PERF-3 Option B: Skip text analysis for trusted/admin users
        // Spacing is not a critical check - it's a heuristic and should skip for trusted users
        if (request.IsUserTrusted || request.IsUserAdmin)
        {
            logger.LogDebug(
                "Skipping Spacing check for user {UserId}: User is {UserType}",
                request.User.Id,
                request.IsUserTrusted ? "trusted" : "admin");
            return false;
        }

        return true;
    }

    public ValueTask<ContentCheckResponseV2> CheckAsync(ContentCheckRequestBase request)
    {
        var startTimestamp = Stopwatch.GetTimestamp();
        var req = (SpacingCheckRequest)request;

        try
        {
            var (hasSuspiciousSpacing, details) = CheckForSuspiciousSpacing(
                req.Message, req.SuspiciousRatioThreshold, req.ShortWordLength, req.MinWordsCount);

            if (hasSuspiciousSpacing)
            {
                return ValueTask.FromResult(new ContentCheckResponseV2
                {
                    CheckName = CheckName,
                    Score = ScoringConstants.ScoreFormattingAnomaly,
                    Abstained = false,
                    Details = details,
                    ProcessingTimeMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds
                });
            }

            return ValueTask.FromResult(new ContentCheckResponseV2
            {
                CheckName = CheckName,
                Score = 0.0,
                Abstained = true,
                Details = "No suspicious spacing patterns detected",
                ProcessingTimeMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in SpacingSpamCheckV2");
            return ValueTask.FromResult(new ContentCheckResponseV2
            {
                CheckName = CheckName,
                Score = 0.0,
                Abstained = true,
                Details = $"Error: {ex.Message}",
                Error = ex,
                ProcessingTimeMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds
            });
        }
    }

    private static (bool hasSuspiciousSpacing, string details) CheckForSuspiciousSpacing(
        string message, double threshold, int shortWordLength, int minWordsCount)
    {
        var words = message.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Skip messages with too few words to analyze reliably
        // Clamp to at least 1 to prevent division by zero if config has MinWordsCount=0
        var effectiveMinWords = Math.Max(1, minWordsCount);
        if (words.Length < effectiveMinWords)
            return (false, $"Too few words ({words.Length} < {effectiveMinWords})");

        var shortWords = words.Count(w => w.Length <= shortWordLength);
        var suspiciousRatio = (double)shortWords / words.Length;

        if (suspiciousRatio > threshold)
        {
            return (true, $"Suspicious short word ratio: {suspiciousRatio:P0}");
        }

        return (false, "No suspicious spacing detected");
    }
}
