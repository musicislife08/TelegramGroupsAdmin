using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.ContentDetection.Abstractions;
using TelegramGroupsAdmin.ContentDetection.Constants;
using TelegramGroupsAdmin.ContentDetection.Helpers;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Services;
using TelegramGroupsAdmin.Core.Extensions;

namespace TelegramGroupsAdmin.ContentDetection.Checks;

/// <summary>
/// V2 stop words check with proper abstention support
/// Key fix: Returns Score=0 (abstain) when 0 matches instead of "Clean 20%" vote
/// Scoring: 0.5-2.0 points based on match count and severity
/// </summary>
public class StopWordsContentCheckV2(
    ILogger<StopWordsContentCheckV2> logger,
    IDbContextFactory<AppDbContext> dbContextFactory,
    ITokenizerService tokenizerService) : IContentCheckV2
{
    public CheckName CheckName => CheckName.StopWords;

    public bool ShouldExecute(ContentCheckRequest request)
    {
        // Skip empty messages
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return false;
        }

        // PERF-3 Option B: Skip database queries and text analysis for trusted/admin users
        // StopWords is not a critical check - it requires database queries and should skip for trusted users
        if (request.IsUserTrusted || request.IsUserAdmin)
        {
            logger.LogDebug(
                "Skipping StopWords check for {User}: User is {UserType}",
                request.User.ToLogDebug(),
                request.IsUserTrusted ? "trusted" : "admin");
            return false;
        }

        return true;
    }

    public async ValueTask<ContentCheckResponseV2> CheckAsync(ContentCheckRequestBase request)
    {
        var startTimestamp = Stopwatch.GetTimestamp();
        var req = (StopWordsCheckRequest)request;

        try
        {
            // Load stop words from database
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(req.CancellationToken);
            var stopWords = await dbContext.StopWords
                .AsNoTracking()
                .Where(w => w.Enabled)
                .OrderBy(w => w.Id)
                .Take(StopWordsConstants.MaxStopWords)
                .Select(w => w.Word)
                .ToListAsync(req.CancellationToken);

            if (stopWords.Count == 0)
                return new ContentCheckResponseV2
                {
                    CheckName = CheckName,
                    Score = 0.0,
                    Abstained = true,
                    Details = "No stop words configured",
                    ProcessingTimeMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds
                };

            var stopWordsSet = new HashSet<string>(stopWords, StringComparer.OrdinalIgnoreCase);
            var foundMatches = new List<string>();

            // Check message text
            var processedMessage = tokenizerService.RemoveEmojis(req.Message);
            var messageMatches = CheckTextForStopWords(processedMessage, stopWordsSet, "message");
            foundMatches.AddRange(messageMatches);

            // Check username (display name)
            if (!string.IsNullOrWhiteSpace(req.User.DisplayName))
            {
                var usernameMatches = CheckTextForStopWords(req.User.DisplayName, stopWordsSet, "username");
                foundMatches.AddRange(usernameMatches);
            }

            // Check userID
            if (req.User.Id != 0)
            {
                var userIdMatches = CheckTextForStopWords(req.User.Id.ToString(), stopWordsSet, "userID");
                foundMatches.AddRange(userIdMatches);
            }

            // V2 FIX: Abstain when 0 matches (instead of Clean 20%)
            if (foundMatches.Count == 0)
            {
                return new ContentCheckResponseV2
                {
                    CheckName = CheckName,
                    Score = 0.0,
                    Abstained = true,
                    Details = "No stop words detected",
                    ProcessingTimeMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds
                };
            }

            // Calculate score based on match severity (SpamAssassin-style)
            var score = CalculateScore(foundMatches.Count, req.Message.Length);

            var details = $"Found stop words: {string.Join(", ", foundMatches.Take(3))}" +
                         (foundMatches.Count > 3 ? $" (+{foundMatches.Count - 3} more)" : "");
            
            return new ContentCheckResponseV2
            {
                CheckName = CheckName,
                Score = score,
                Abstained = false,
                Details = details,
                ProcessingTimeMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in StopWordsSpamCheckV2 for {User}", req.User.ToLogDebug());
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

    private static List<string> CheckTextForStopWords(string text, HashSet<string> stopWords, string fieldType)
    {
        var matches = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
            return matches;

        var lowerText = text.ToLowerInvariant();

        foreach (var stopWord in stopWords)
        {
            if (lowerText.Contains(stopWord, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add($"{stopWord} (in {fieldType})");
            }
        }

        return matches;
    }

    /// <summary>
    /// V2 scoring: Map match count to points (0.5-2.0)
    /// Research guidance: keywords = 0.5-2.0 points depending on severity
    /// </summary>
    private static double CalculateScore(int matchCount, int messageLength)
    {
        // 3+ matches = severe (especially username/userID matches)
        if (matchCount >= 3)
            return ScoringConstants.ScoreStopWordsSevere;

        // 2 matches = moderate
        if (matchCount == 2)
            return ScoringConstants.ScoreStopWordsModerate;

        // Single match in short message (<50 chars) = moderate
        if (matchCount == 1 && messageLength < 50)
            return ScoringConstants.ScoreStopWordsModerate;

        // Single match in long message (>200 chars) = mild
        if (matchCount == 1 && messageLength > 200)
            return ScoringConstants.ScoreStopWordsMild;

        // Default: 1 match in normal-length message = moderate
        return ScoringConstants.ScoreStopWordsModerate;
    }
}
