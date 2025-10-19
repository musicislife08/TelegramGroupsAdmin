using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.ContentDetection.Abstractions;
using TelegramGroupsAdmin.ContentDetection.Models;

namespace TelegramGroupsAdmin.ContentDetection.Checks;

/// <summary>
/// Detects invisible/zero-width Unicode characters used by spammers to evade filters
/// Excludes Zero Width Joiner when used in legitimate emoji sequences
/// </summary>
public class InvisibleCharsSpamCheck : IContentCheck
{
    private readonly ILogger<InvisibleCharsSpamCheck> _logger;

    public string CheckName => "InvisibleChars";

    public InvisibleCharsSpamCheck(ILogger<InvisibleCharsSpamCheck> logger)
    {
        _logger = logger;
    }

    public bool ShouldExecute(ContentCheckRequest request)
    {
        // Skip empty messages
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return false;
        }

        return true;
    }

    public Task<ContentCheckResponse> CheckAsync(ContentCheckRequestBase request)
    {
        var req = (InvisibleCharsCheckRequest)request;

        try
        {
            var count = CountInvisibleCharacters(req.Message);

            if (count > 0)
            {
                _logger.LogDebug("InvisibleChars check for user {UserId}: Found {Count} invisible characters",
                    req.UserId, count);

                return Task.FromResult(new ContentCheckResponse
                {
                    CheckName = CheckName,
                    Result = CheckResultType.Spam,
                    Details = $"Contains {count} invisible/hidden characters",
                    Confidence = 90
                });
            }

            // Phase 2.6: Asymmetric confidence scoring
            // Simple checks have low confidence when NOT spam (absence of evidence ≠ strong evidence)
            // 20% confidence in "not spam" result (vs 0% before)
            return Task.FromResult(new ContentCheckResponse
            {
                CheckName = CheckName,
                Result = CheckResultType.Clean,
                Details = "No invisible characters detected",
                Confidence = 20
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "InvisibleChars check failed for user {UserId}", req.UserId);
            return Task.FromResult(new ContentCheckResponse
            {
                CheckName = CheckName,
                Result = CheckResultType.Clean,
                Details = "Check failed due to error",
                Confidence = 0,
                Error = ex
            });
        }
    }

    /// <summary>
    /// Count invisible/zero-width characters that might be used to hide spam
    /// Excludes Zero Width Joiner when message contains emojis (legitimate use)
    /// </summary>
    private int CountInvisibleCharacters(string message)
    {
        var suspiciousInvisibleChars = new[]
        {
            '\u200B', // Zero Width Space
            '\u200C', // Zero Width Non-Joiner
            '\u2060', // Word Joiner
            '\uFEFF'  // Zero Width No-Break Space
        };

        // Count suspicious invisible chars (excluding ZWJ for now)
        var count = message.Count(c => suspiciousInvisibleChars.Contains(c));

        // Check for Zero Width Joiner (\u200D)
        // ZWJ is legitimate in emoji sequences (e.g., 🤷‍♂️), but spam in plain text
        // Simple approach: If message contains emojis, ignore ZWJ; otherwise count it
        var hasZwj = message.Contains('\u200D');
        if (hasZwj)
        {
            var hasEmojis = ContainsEmoji(message);

            // Only count ZWJ if message has NO emojis (likely spam technique)
            if (!hasEmojis)
            {
                count += message.Count(c => c == '\u200D');
            }
        }

        return count;
    }

    /// <summary>
    /// Check if message contains emoji characters (simplified detection)
    /// </summary>
    private bool ContainsEmoji(string message)
    {
        // Check for common emoji ranges
        // This covers most modern emojis including emoticons, symbols, and pictographs
        foreach (var c in message)
        {
            var code = (int)c;

            // Main emoji ranges
            if ((code >= 0x1F300 && code <= 0x1FAF8) ||  // Emoticons, symbols, pictographs
                (code >= 0x2600 && code <= 0x27BF) ||    // Dingbats and misc symbols
                (code >= 0x1F900 && code <= 0x1F9FF) ||  // Supplemental symbols
                char.IsHighSurrogate(c))                  // High surrogates used in emoji pairs
            {
                return true;
            }
        }

        return false;
    }
}
