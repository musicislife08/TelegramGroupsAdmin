using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.SpamDetection.Abstractions;
using TelegramGroupsAdmin.SpamDetection.Configuration;
using TelegramGroupsAdmin.SpamDetection.Models;
using TelegramGroupsAdmin.SpamDetection.Repositories;

namespace TelegramGroupsAdmin.SpamDetection.Checks;

/// <summary>
/// Simplified spam check that detects core spacing patterns: ratios, invisible chars, letter spacing
/// Streamlined version focusing on the most effective patterns
/// </summary>
public class SpacingSpamCheck : ISpamCheck
{
    private readonly ILogger<SpacingSpamCheck> _logger;
    private readonly ISpamDetectionConfigRepository _configRepository;

    public string CheckName => "Spacing";

    public SpacingSpamCheck(ILogger<SpacingSpamCheck> logger, ISpamDetectionConfigRepository configRepository)
    {
        _logger = logger;
        _configRepository = configRepository;
    }

    /// <summary>
    /// Check if spacing check should be executed
    /// </summary>
    public bool ShouldExecute(SpamCheckRequest request)
    {
        // Skip empty or very short messages
        if (string.IsNullOrWhiteSpace(request.Message) || request.Message.Length < 20)
        {
            return false;
        }

        // Check if enabled is done in CheckAsync since we need to load config from DB
        return true;
    }

    /// <summary>
    /// Execute spacing spam check
    /// </summary>
    public async Task<SpamCheckResponse> CheckAsync(SpamCheckRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            // Load config from database
            var config = await _configRepository.GetGlobalConfigAsync(cancellationToken);

            // Check if this check is enabled
            if (!config.Spacing.Enabled)
            {
                return new SpamCheckResponse
                {
                    CheckName = CheckName,
                    IsSpam = false,
                    Details = "Check disabled",
                    Confidence = 0
                };
            }

            var analysis = AnalyzeSpacing(request.Message, config);
            var isSpam = analysis.IsSuspicious;
            var confidence = CalculateConfidence(analysis);

            _logger.LogDebug("Spacing check for user {UserId}: SpaceRatio={SpaceRatio:F3}, ShortWordRatio={ShortWordRatio:F3}, IsSuspicious={IsSuspicious}",
                request.UserId, analysis.SpaceRatio, analysis.ShortWordRatio, analysis.IsSuspicious);

            return new SpamCheckResponse
            {
                CheckName = CheckName,
                IsSpam = isSpam,
                Details = analysis.Details,
                Confidence = confidence
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Spacing check failed for user {UserId}", request.UserId);
            return new SpamCheckResponse
            {
                CheckName = CheckName,
                IsSpam = false,
                Details = "Spacing check failed due to error",
                Confidence = 0,
                Error = ex
            };
        }
    }

    /// <summary>
    /// Analyze core spacing patterns in the message (simplified approach)
    /// </summary>
    private SpacingAnalysis AnalyzeSpacing(string message, SpamDetectionConfig config)
    {
        // Extract words for basic analysis
        var words = ExtractWords(message);
        var wordCount = words.Length;

        if (wordCount < config.Spacing.MinWordsCount)
        {
            return new SpacingAnalysis
            {
                SpaceRatio = 0,
                ShortWordRatio = 0,
                AvgWordLength = 0,
                SuspiciousPatterns = [],
                IsSuspicious = false,
                Details = "Message too short for spacing analysis"
            };
        }

        // Core ratio 1: Space ratio (spaces vs total characters)
        var spaceCount = message.Count(c => c == ' ');
        var totalChars = message.Length;
        var spaceRatio = (double)spaceCount / totalChars;

        // Core ratio 2: Short word ratio
        var shortWords = words.Count(w => w.Length <= config.Spacing.ShortWordLength);
        var shortWordRatio = (double)shortWords / wordCount;

        var avgWordLength = words.Average(w => w.Length);

        // Detect core suspicious patterns (simplified)
        var suspiciousPatterns = DetectCoreSuspiciousPatterns(message);

        // Determine if spacing is suspicious based on core metrics
        var isSuspicious = spaceRatio >= config.Spacing.SpaceRatioThreshold ||
                          shortWordRatio >= config.Spacing.ShortWordRatioThreshold ||
                          suspiciousPatterns.Any();

        var details = BuildSpacingDetails(spaceRatio, shortWordRatio, avgWordLength, suspiciousPatterns);

        return new SpacingAnalysis
        {
            SpaceRatio = spaceRatio,
            ShortWordRatio = shortWordRatio,
            AvgWordLength = avgWordLength,
            SuspiciousPatterns = suspiciousPatterns,
            IsSuspicious = isSuspicious,
            Details = details
        };
    }

    /// <summary>
    /// Extract words from message, preserving original spacing context
    /// </summary>
    private static string[] ExtractWords(string message)
    {
        // Remove URLs and mentions to focus on actual text content
        var cleanMessage = Regex.Replace(message, @"https?://[^\s]+", "", RegexOptions.IgnoreCase);
        cleanMessage = Regex.Replace(cleanMessage, @"@\w+", "", RegexOptions.IgnoreCase);

        // Split by spaces but preserve word structure
        return cleanMessage.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 1)
            .Where(w => !IsOnlyPunctuation(w))
            .ToArray();
    }

    /// <summary>
    /// Check if string contains only punctuation characters
    /// </summary>
    private static bool IsOnlyPunctuation(string word)
    {
        return word.All(c => char.IsPunctuation(c) || char.IsSymbol(c));
    }

    /// <summary>
    /// Detect core suspicious spacing patterns (simplified to most effective)
    /// </summary>
    private static List<string> DetectCoreSuspiciousPatterns(string message)
    {
        var patterns = new List<string>();

        // Core pattern 1: Letter spacing (l i k e  t h i s)
        if (HasLetterSpacing(message))
        {
            patterns.Add("Letters artificially separated");
        }

        // Core pattern 2: Invisible/zero-width characters
        var invisibleCount = CountInvisibleCharacters(message);
        if (invisibleCount > 0)
        {
            patterns.Add($"Invisible characters ({invisibleCount})");
        }

        // Core pattern 3: Unusual Unicode spaces
        if (HasUnusualSpaces(message))
        {
            patterns.Add("Non-standard spacing characters");
        }

        return patterns;
    }




    /// <summary>
    /// Detect letters artificially separated by spaces
    /// </summary>
    private static bool HasLetterSpacing(string message)
    {
        // Look for pattern of 4+ single letters separated by single spaces
        // Match: "l i k e  t h i s" but not "I got my car"
        // Requiring 4+ letters reduces false positives from natural English with "I" and "a"
        return Regex.IsMatch(message, @"\b[a-zA-Z]\s[a-zA-Z]\s[a-zA-Z]\s[a-zA-Z]", RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// Count invisible/zero-width characters
    /// </summary>
    private static int CountInvisibleCharacters(string message)
    {
        var invisibleChars = new[]
        {
            '\u200B', // Zero Width Space
            '\u200C', // Zero Width Non-Joiner
            '\u200D', // Zero Width Joiner
            '\u2060', // Word Joiner
            '\uFEFF'  // Zero Width No-Break Space
        };

        return message.Count(c => invisibleChars.Contains(c));
    }

    /// <summary>
    /// Detect unusual Unicode spacing characters
    /// </summary>
    private static bool HasUnusualSpaces(string message)
    {
        var unusualSpaces = new[]
        {
            '\u00A0', // Non-breaking space
            '\u2000', // En quad
            '\u2001', // Em quad
            '\u2002', // En space
            '\u2003', // Em space
            '\u2009', // Thin space
            '\u200A', // Hair space
            '\u202F', // Narrow no-break space
            '\u3000'  // Ideographic space
        };

        return message.Any(c => unusualSpaces.Contains(c));
    }

    /// <summary>
    /// Build human-readable details about spacing analysis
    /// </summary>
    private static string BuildSpacingDetails(double spaceRatio, double shortWordRatio,
                                            double avgWordLength, List<string> suspiciousPatterns)
    {
        var details = new List<string>
        {
            $"Space ratio: {spaceRatio:F3}",
            $"Short word ratio: {shortWordRatio:F3}",
            $"Avg word length: {avgWordLength:F1}"
        };

        if (suspiciousPatterns.Any())
        {
            details.Add($"Patterns: {string.Join(", ", suspiciousPatterns)}");
        }

        return string.Join("; ", details);
    }

    /// <summary>
    /// Calculate confidence score based on simplified spacing analysis
    /// Reduces confidence for pattern-only detections to minimize false positives
    /// </summary>
    private static int CalculateConfidence(SpacingAnalysis analysis)
    {
        if (!analysis.IsSuspicious)
        {
            return 0;
        }

        var confidence = 0;
        var hasHighRatios = false;

        // Core ratio scoring (simplified)
        if (analysis.SpaceRatio >= 0.4)
        {
            confidence += 40;
            hasHighRatios = true;
        }
        else if (analysis.SpaceRatio >= 0.3)
        {
            confidence += 25;
            hasHighRatios = true;
        }

        if (analysis.ShortWordRatio >= 0.8)
        {
            confidence += 35;
            hasHighRatios = true;
        }
        else if (analysis.ShortWordRatio >= 0.7)
        {
            confidence += 20;
            hasHighRatios = true;
        }

        // Pattern scoring
        var patternScore = 0;
        foreach (var pattern in analysis.SuspiciousPatterns)
        {
            if (pattern.Contains("invisible", StringComparison.OrdinalIgnoreCase))
            {
                patternScore += 50; // Invisible chars are highly suspicious
            }
            else if (pattern.Contains("separated", StringComparison.OrdinalIgnoreCase))
            {
                patternScore += 40; // Letter spacing is very suspicious
            }
            else
            {
                patternScore += 20; // Other patterns
            }
        }

        // Apply pattern score reduction if no high ratios
        // Pattern-only detections get 50% confidence penalty to reduce false positives
        // This allows OpenAI veto and other checks to have more weight
        if (!hasHighRatios && analysis.SuspiciousPatterns.Any())
        {
            patternScore = (int)(patternScore * 0.5);
        }

        confidence += patternScore;

        return Math.Min(100, confidence);
    }
}

/// <summary>
/// Results of spacing pattern analysis
/// </summary>
internal record SpacingAnalysis
{
    public required double SpaceRatio { get; init; }
    public required double ShortWordRatio { get; init; }
    public required double AvgWordLength { get; init; }
    public required List<string> SuspiciousPatterns { get; init; }
    public required bool IsSuspicious { get; init; }
    public required string Details { get; init; }
}