using TelegramGroupsAdmin.Core.Utilities;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Service for calculating text similarity using Jaccard index.
/// Uses shared TextTokenizer for consistent word extraction.
/// </summary>
public class TextSimilarityService
{
    /// <summary>
    /// Calculate Jaccard similarity between two texts (0.0 = no overlap, 1.0 = identical)
    /// Uses word-based tokenization (lowercase, split on whitespace + punctuation)
    /// </summary>
    public double CalculateSimilarity(string text1, string text2)
    {
        if (string.IsNullOrWhiteSpace(text1) || string.IsNullOrWhiteSpace(text2))
            return 0.0;

        var tokens1 = TextTokenizer.TokenizeToSet(text1);
        var tokens2 = TextTokenizer.TokenizeToSet(text2);

        if (tokens1.Count == 0 || tokens2.Count == 0)
            return 0.0;

        // Jaccard index: |A ∩ B| / |A ∪ B|
        var intersection = tokens1.Intersect(tokens2).Count();
        var union = tokens1.Union(tokens2).Count();

        return union > 0 ? (double)intersection / union : 0.0;
    }
}
