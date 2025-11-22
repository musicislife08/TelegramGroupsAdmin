using System.Text.RegularExpressions;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Service for calculating text similarity using Jaccard index
/// Reuses tokenization logic from SimilaritySpamCheckV2 for consistency
/// </summary>
public partial class TextSimilarityService
{
    /// <summary>
    /// Calculate Jaccard similarity between two texts (0.0 = no overlap, 1.0 = identical)
    /// Uses word-based tokenization (lowercase, split on whitespace + punctuation)
    /// </summary>
    public double CalculateSimilarity(string text1, string text2)
    {
        if (string.IsNullOrWhiteSpace(text1) || string.IsNullOrWhiteSpace(text2))
            return 0.0;

        var tokens1 = TokenizeText(text1);
        var tokens2 = TokenizeText(text2);

        if (tokens1.Count == 0 || tokens2.Count == 0)
            return 0.0;

        // Jaccard index: |A ∩ B| / |A ∪ B|
        var intersection = tokens1.Intersect(tokens2).Count();
        var union = tokens1.Union(tokens2).Count();

        return union > 0 ? (double)intersection / union : 0.0;
    }

    /// <summary>
    /// Tokenize text into normalized word set (lowercase, alphanumeric only)
    /// Matches SimilaritySpamCheckV2 tokenization for consistency
    /// </summary>
    private HashSet<string> TokenizeText(string text)
    {
        // Lowercase and split on whitespace/punctuation
        var normalized = text.ToLowerInvariant();
        var words = WhitespaceAndPunctuation().Split(normalized);

        // Filter out empty strings and very short tokens (single characters)
        return words
            .Where(w => w.Length > 1)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Source-generated regex for splitting text on whitespace and punctuation
    /// Matches SimilaritySpamCheckV2 pattern
    /// </summary>
    [GeneratedRegex(@"[\s\p{P}]+", RegexOptions.Compiled)]
    private static partial Regex WhitespaceAndPunctuation();
}
