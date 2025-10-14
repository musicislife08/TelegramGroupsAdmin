using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.SpamDetection.Abstractions;
using TelegramGroupsAdmin.SpamDetection.Configuration;
using TelegramGroupsAdmin.SpamDetection.Models;
using TelegramGroupsAdmin.SpamDetection.Repositories;
using TelegramGroupsAdmin.SpamDetection.Services;

namespace TelegramGroupsAdmin.SpamDetection.Checks;

/// <summary>
/// Spam check that looks for stop words in message text, username, and userID
/// Enhanced version based on tg-spam with database storage and emoji preprocessing
/// </summary>
public class StopWordsSpamCheck : ISpamCheck
{
    private readonly ILogger<StopWordsSpamCheck> _logger;
    private readonly ISpamDetectionConfigRepository _configRepository;
    private readonly IStopWordsRepository _stopWordsRepository;
    private readonly ITokenizerService _tokenizerService;
    private HashSet<string>? _cachedStopWords;
    private DateTime _lastCacheUpdate = DateTime.MinValue;
    private readonly TimeSpan _cacheRefreshInterval = TimeSpan.FromMinutes(5);


    public string CheckName => "StopWords";

    public StopWordsSpamCheck(
        ILogger<StopWordsSpamCheck> logger,
        ISpamDetectionConfigRepository configRepository,
        IStopWordsRepository stopWordsRepository,
        ITokenizerService tokenizerService)
    {
        _logger = logger;
        _configRepository = configRepository;
        _stopWordsRepository = stopWordsRepository;
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
    /// Execute stop words spam check on message, username, and userID
    /// </summary>
    public async Task<SpamCheckResponse> CheckAsync(SpamCheckRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            // Load config from database
            var config = await _configRepository.GetGlobalConfigAsync(cancellationToken);

            // Check if this check is enabled
            if (!config.StopWords.Enabled)
            {
                return new SpamCheckResponse
                {
                    CheckName = CheckName,
                    IsSpam = false,
                    Details = "Check disabled",
                    Confidence = 0
                };
            }

            // Get stop words from database (with caching)
            var stopWords = await GetStopWordsAsync(cancellationToken);
            if (!stopWords.Any())
            {
                return new SpamCheckResponse
                {
                    CheckName = CheckName,
                    IsSpam = false,
                    Details = "No stop words configured",
                    Confidence = 0
                };
            }

            var foundMatches = new List<string>();

            // Check message text (with emoji preprocessing)
            var processedMessage = _tokenizerService.RemoveEmojis(request.Message ?? "");
            var messageMatches = CheckTextForStopWords(processedMessage, stopWords, "message");
            foundMatches.AddRange(messageMatches);

            // Check username
            if (!string.IsNullOrWhiteSpace(request.UserName))
            {
                var usernameMatches = CheckTextForStopWords(request.UserName, stopWords, "username");
                foundMatches.AddRange(usernameMatches);
            }

            // Check userID (convert to string for checking)
            if (!string.IsNullOrWhiteSpace(request.UserId))
            {
                var userIdMatches = CheckTextForStopWords(request.UserId, stopWords, "userID");
                foundMatches.AddRange(userIdMatches);
            }

            // Calculate confidence based on matches
            var confidence = CalculateConfidence(foundMatches.Count, request.Message?.Length ?? 0);
            var isSpam = confidence >= config.StopWords.ConfidenceThreshold;

            var details = foundMatches.Any()
                ? $"Found stop words: {string.Join(", ", foundMatches.Take(3))}" + (foundMatches.Count > 3 ? $" (+{foundMatches.Count - 3} more)" : "")
                : "No stop words detected";

            _logger.LogDebug("StopWords check for user {UserId}: Found {MatchCount} matches, confidence {Confidence}%",
                request.UserId, foundMatches.Count, confidence);

            return new SpamCheckResponse
            {
                CheckName = CheckName,
                IsSpam = isSpam,
                Details = details,
                Confidence = confidence
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "StopWords check failed for user {UserId}", request.UserId);
            return new SpamCheckResponse
            {
                CheckName = CheckName,
                IsSpam = false, // Fail open
                Details = "StopWords check failed due to error",
                Confidence = 0,
                Error = ex
            };
        }
    }

    /// <summary>
    /// Get stop words from database with caching
    /// </summary>
    private async Task<HashSet<string>> GetStopWordsAsync(CancellationToken cancellationToken)
    {
        // Check if cache needs refresh
        if (_cachedStopWords == null || DateTime.UtcNow - _lastCacheUpdate > _cacheRefreshInterval)
        {
            try
            {
                var words = await _stopWordsRepository.GetEnabledStopWordsAsync(cancellationToken);
                _cachedStopWords = new HashSet<string>(words, StringComparer.OrdinalIgnoreCase);
                _lastCacheUpdate = DateTime.UtcNow;

                _logger.LogDebug("Refreshed stop words cache with {Count} words", _cachedStopWords.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to refresh stop words cache");
                // Return existing cache or empty set if no cache exists
                return _cachedStopWords ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        return _cachedStopWords ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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