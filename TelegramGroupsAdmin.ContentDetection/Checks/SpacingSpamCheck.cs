using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.ContentDetection.Abstractions;
using TelegramGroupsAdmin.ContentDetection.Constants;
using TelegramGroupsAdmin.ContentDetection.Helpers;
using TelegramGroupsAdmin.ContentDetection.Models;

namespace TelegramGroupsAdmin.ContentDetection.Checks;

/// <summary>
/// Simplified spam check that detects core spacing patterns: ratios, invisible chars, letter spacing
/// Streamlined version focusing on the most effective patterns
/// Config comes from strongly-typed request - no database access needed
/// </summary>
public partial class SpacingSpamCheck(ILogger<SpacingSpamCheck> logger) : IContentCheck
{
    public CheckName CheckName => CheckName.Spacing;

    // Regex for removing URLs from message text
    [GeneratedRegex(@"https?://[^\s]+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRemovalRegex();

    // Regex for removing @mentions from message text
    [GeneratedRegex(@"@\w+", RegexOptions.IgnoreCase)]
    private static partial Regex MentionRemovalRegex();

    // Regex for detecting letter spacing pattern (4+ single letters separated by spaces)
    [GeneratedRegex(@"\b[a-zA-Z]\s[a-zA-Z]\s[a-zA-Z]\s[a-zA-Z]", RegexOptions.IgnoreCase)]
    private static partial Regex LetterSpacingRegex();

    /// <summary>
    /// Check if spacing check should be executed
    /// </summary>
    public bool ShouldExecute(ContentCheckRequest request)
    {
        // Skip empty or very short messages
        if (string.IsNullOrWhiteSpace(request.Message) || request.Message.Length < 20)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Execute spacing spam check with strongly-typed request
    /// Config comes from request - no database access needed
    /// </summary>
    public ValueTask<ContentCheckResponse> CheckAsync(ContentCheckRequestBase request)
    {
        var req = (SpacingCheckRequest)request;

        try
        {
            var analysis = AnalyzeSpacing(req.Message, req.ConfidenceThreshold, req.SuspiciousRatioThreshold);
            var isSpam = analysis.IsSuspicious;
            var result = isSpam ? CheckResultType.Spam : CheckResultType.Clean;
            var confidence = CalculateConfidence(analysis);

            return new ValueTask<ContentCheckResponse>(new ContentCheckResponse
            {
                CheckName = CheckName,
                Result = result,
                Details = analysis.Details,
                Confidence = confidence
            });
        }
        catch (Exception ex)
        {
            return new ValueTask<ContentCheckResponse>(ContentCheckHelpers.CreateFailureResponse(CheckName, ex, logger, req.UserId));
        }
    }

    /// <summary>
    /// Analyze core spacing patterns in the message (simplified approach)
    /// </summary>
    private SpacingAnalysis AnalyzeSpacing(string message, int confidenceThreshold, double suspiciousRatioThreshold)
    {
        // Extract words for basic analysis
        var words = ExtractWords(message);
        var wordCount = words.Length;

        // Default values from old config: MinWordsCount=5, ShortWordLength=2
        const int minWordsCount = 5;
        const int shortWordLength = 2;

        if (wordCount < minWordsCount)
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
        var shortWords = words.Count(w => w.Length <= shortWordLength);
        var shortWordRatio = (double)shortWords / wordCount;

        var avgWordLength = words.Average(w => w.Length);

        // Detect core suspicious patterns (simplified)
        var suspiciousPatterns = DetectCoreSuspiciousPatterns(message);

        // Determine if spacing is suspicious based on core metrics
        // Use suspiciousRatioThreshold for both ratios (defaults to 0.35 from old config)
        var isSuspicious = spaceRatio >= suspiciousRatioThreshold ||
                          shortWordRatio >= suspiciousRatioThreshold ||
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
        var cleanMessage = UrlRemovalRegex().Replace(message, "");
        cleanMessage = MentionRemovalRegex().Replace(cleanMessage, "");

        // Split by spaces but preserve word structure
        return cleanMessage.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
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
        return LetterSpacingRegex().IsMatch(message);
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
    /// Phase 2.6: Asymmetric confidence - returns 20% when NOT suspicious
    /// Reduces confidence for pattern-only detections to minimize false positives
    /// </summary>
    private static int CalculateConfidence(SpacingAnalysis analysis)
    {
        if (!analysis.IsSuspicious)
        {
            // Phase 2.6: Simple checks return 20% confidence when NOT spam
            return 20;
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