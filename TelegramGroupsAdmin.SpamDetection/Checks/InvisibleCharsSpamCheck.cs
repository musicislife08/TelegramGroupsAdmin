using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.SpamDetection.Abstractions;
using TelegramGroupsAdmin.SpamDetection.Configuration;
using TelegramGroupsAdmin.SpamDetection.Models;
using TelegramGroupsAdmin.SpamDetection.Repositories;

namespace TelegramGroupsAdmin.SpamDetection.Checks;

/// <summary>
/// Detects invisible/zero-width Unicode characters used by spammers to evade filters
/// Excludes Zero Width Joiner when used in legitimate emoji sequences
/// </summary>
public class InvisibleCharsSpamCheck : ISpamCheck
{
    private readonly ILogger<InvisibleCharsSpamCheck> _logger;
    private readonly ISpamDetectionConfigRepository _configRepository;

    public string CheckName => "InvisibleChars";

    public InvisibleCharsSpamCheck(ILogger<InvisibleCharsSpamCheck> logger, ISpamDetectionConfigRepository configRepository)
    {
        _logger = logger;
        _configRepository = configRepository;
    }

    public bool ShouldExecute(SpamCheckRequest request)
    {
        // Skip empty messages
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return false;
        }

        return true;
    }

    public async Task<SpamCheckResponse> CheckAsync(SpamCheckRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            // Load config to check if enabled
            var config = await _configRepository.GetGlobalConfigAsync(cancellationToken);

            if (!config.InvisibleChars.Enabled)
            {
                return new SpamCheckResponse
                {
                    CheckName = CheckName,
                    IsSpam = false,
                    Details = "Check disabled",
                    Confidence = 0
                };
            }

            var count = CountInvisibleCharacters(request.Message);

            if (count > 0)
            {
                _logger.LogDebug("InvisibleChars check for user {UserId}: Found {Count} invisible characters",
                    request.UserId, count);

                return new SpamCheckResponse
                {
                    CheckName = CheckName,
                    IsSpam = true,
                    Details = $"Contains {count} invisible/hidden characters",
                    Confidence = 90
                };
            }

            // Phase 2.6: Asymmetric confidence scoring
            // Simple checks have low confidence when NOT spam (absence of evidence ≠ strong evidence)
            // 20% confidence in "not spam" result (vs 0% before)
            return new SpamCheckResponse
            {
                CheckName = CheckName,
                IsSpam = false,
                Details = "No invisible characters detected",
                Confidence = 20
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "InvisibleChars check failed for user {UserId}", request.UserId);
            return new SpamCheckResponse
            {
                CheckName = CheckName,
                IsSpam = false,
                Details = "Check failed due to error",
                Confidence = 0,
                Error = ex
            };
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
