using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.SpamDetection.Abstractions;
using TelegramGroupsAdmin.SpamDetection.Models;
using TelegramGroupsAdmin.SpamDetection.Services;

namespace TelegramGroupsAdmin.SpamDetection.Checks;

/// <summary>
/// Spam check that looks for stop words in message text, username, and userID
/// Enhanced version based on tg-spam with database storage and emoji preprocessing
/// Engine orchestrates config loading - check manages its own DB access with guardrails
/// </summary>
public class StopWordsSpamCheck : ISpamCheck
{
    private const int MAX_STOP_WORDS = 10_000; // Guardrail: cap stop words query

    private readonly ILogger<StopWordsSpamCheck> _logger;
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly ITokenizerService _tokenizerService;

    public string CheckName => "StopWords";

    public StopWordsSpamCheck(
        ILogger<StopWordsSpamCheck> logger,
        IDbContextFactory<AppDbContext> dbContextFactory,
        ITokenizerService tokenizerService)
    {
        _logger = logger;
        _dbContextFactory = dbContextFactory;
        _tokenizerService = tokenizerService;
    }

    /// <summary>
    /// Check if stop words check should be executed
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
    /// Execute stop words spam check with strongly-typed request
    /// Config comes from request - check loads stop words from DB with guardrails
    /// </summary>
    public async Task<SpamCheckResponse> CheckAsync(SpamCheckRequestBase request)
    {
        var req = (StopWordsCheckRequest)request;

        try
        {
            // Load stop words from database with guardrail
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(req.CancellationToken);
            var stopWords = await dbContext.StopWords
                .AsNoTracking()
                .Where(w => w.Enabled)
                .Take(MAX_STOP_WORDS) // ← Guardrail
                .Select(w => w.Word)
                .ToListAsync(req.CancellationToken);

            if (stopWords.Count == 0)
            {
                return new SpamCheckResponse
                {
                    CheckName = CheckName,
                    Result = SpamCheckResultType.Clean,
                    Details = "No stop words configured",
                    Confidence = 0
                };
            }

            var stopWordsSet = new HashSet<string>(stopWords, StringComparer.OrdinalIgnoreCase);
            var foundMatches = new List<string>();

            // Check message text (with emoji preprocessing)
            var processedMessage = _tokenizerService.RemoveEmojis(req.Message);
            var messageMatches = CheckTextForStopWords(processedMessage, stopWordsSet, "message");
            foundMatches.AddRange(messageMatches);

            // Check username
            if (!string.IsNullOrWhiteSpace(req.UserName))
            {
                var usernameMatches = CheckTextForStopWords(req.UserName, stopWordsSet, "username");
                foundMatches.AddRange(usernameMatches);
            }

            // Check userID
            if (!string.IsNullOrWhiteSpace(req.UserId))
            {
                var userIdMatches = CheckTextForStopWords(req.UserId, stopWordsSet, "userID");
                foundMatches.AddRange(userIdMatches);
            }

            // Calculate confidence based on matches
            var confidence = CalculateConfidence(foundMatches.Count, req.Message.Length);
            var result = confidence >= req.ConfidenceThreshold ? SpamCheckResultType.Spam : SpamCheckResultType.Clean;

            var details = foundMatches.Any()
                ? $"Found stop words: {string.Join(", ", foundMatches.Take(3))}" + (foundMatches.Count > 3 ? $" (+{foundMatches.Count - 3} more)" : "")
                : "No stop words detected";

            return new SpamCheckResponse
            {
                CheckName = CheckName,
                Result = result,
                Details = details,
                Confidence = confidence
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "StopWords check failed for user {UserId}", req.UserId);
            return new SpamCheckResponse
            {
                CheckName = CheckName,
                Result = SpamCheckResultType.Clean, // Fail open
                Details = "StopWords check failed due to error",
                Confidence = 0,
                Error = ex
            };
        }
    }

    /// <summary>
    /// Check text for stop words using simple Contains() matching
    /// </summary>
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
    /// Calculate confidence score based on stop word matches and message length
    /// Phase 2.6: Asymmetric confidence - low confidence when NO matches (absence of evidence ≠ strong evidence)
    /// </summary>
    private static int CalculateConfidence(int matchCount, int messageLength)
    {
        if (matchCount == 0)
        {
            // Phase 2.6: Simple checks return 20% confidence when NOT spam
            // (vs 0% before). Absence of stop words doesn't strongly indicate "not spam"
            return 20;
        }

        // Base confidence from match count - more aggressive than before
        var baseConfidence = Math.Min(matchCount * 30, 85);

        // Adjust based on message length - shorter messages with matches are more suspicious
        if (messageLength < 50 && matchCount > 0)
        {
            baseConfidence += 15;
        }
        else if (messageLength > 200 && matchCount == 1)
        {
            baseConfidence -= 10; // Single match in long message is less suspicious
        }

        // Multiple matches significantly increase confidence
        if (matchCount >= 2)
        {
            baseConfidence += 20;
        }

        // Username/userID matches are highly suspicious
        if (matchCount >= 3)
        {
            baseConfidence += 25;
        }

        return Math.Max(0, Math.Min(100, baseConfidence));
    }
}