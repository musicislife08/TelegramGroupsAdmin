using System.Text.RegularExpressions;

namespace TelegramGroupsAdmin.Core.Utilities;

/// <summary>
/// Centralized text tokenization utilities for the entire codebase.
/// Provides consistent word extraction for SimHash, Jaccard similarity, Bayes classification, and content checks.
/// </summary>
public static partial class TextTokenizer
{
    /// <summary>
    /// Default minimum token length (tokens shorter than this are filtered out).
    /// Single characters typically add noise without semantic value.
    /// </summary>
    public const int DefaultMinTokenLength = 2;

    /// <summary>
    /// Common stop words to filter out for content analysis.
    /// Based on tg-spam's excluded tokens for Bayes classification.
    /// </summary>
    public static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "for", "are", "but", "not", "you", "all", "can", "had", "her", "was", "one", "our",
        "out", "day", "get", "has", "him", "his", "how", "its", "may", "new", "now", "old", "see", "two",
        "who", "boy", "did", "man", "way", "what", "with", "have", "from", "they", "know", "want", "been",
        "good", "much", "some", "time", "very", "when", "come", "here", "just", "like", "long", "make",
        "many", "over", "such", "take", "than", "them", "well", "were", "will", "your", "before", "being",
        "both", "came", "does", "down", "each", "even", "give", "going", "help", "keep", "last", "made",
        "most", "never", "only", "other", "same", "should", "since", "still", "then", "there", "these",
        "this", "those", "through", "under", "upon", "used", "using", "where", "which", "while", "would"
    };

    #region Regex Patterns

    /// <summary>
    /// Split on whitespace and punctuation. Used by SimHash, Jaccard similarity.
    /// </summary>
    [GeneratedRegex(@"[\s\p{P}]+", RegexOptions.Compiled)]
    private static partial Regex WhitespaceAndPunctuationSplitter();

    /// <summary>
    /// Extract word boundaries (letters, numbers, apostrophes). Used by Bayes, content checks.
    /// </summary>
    [GeneratedRegex(@"\b[\w']+\b", RegexOptions.Compiled)]
    private static partial Regex WordBoundaryExtractor();

    /// <summary>
    /// Match common emoji ranges (simplified for .NET compatibility).
    /// </summary>
    [GeneratedRegex(@"[\u2600-\u26FF]|[\u2700-\u27BF]", RegexOptions.Compiled)]
    private static partial Regex EmojiPattern();

    /// <summary>
    /// Match multiple whitespace characters for cleanup.
    /// </summary>
    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex WhitespaceCleanup();

    #endregion

    #region Basic Tokenization (SimHash, Jaccard)

    /// <summary>
    /// Tokenize text into lowercase words by splitting on whitespace/punctuation.
    /// Returns a HashSet for O(1) lookups and automatic deduplication.
    /// Used by SimHash and Jaccard similarity detection.
    /// </summary>
    /// <param name="text">Text to tokenize</param>
    /// <param name="minLength">Minimum token length (default: 2)</param>
    /// <returns>Set of unique lowercase tokens</returns>
    public static HashSet<string> TokenizeToSet(string? text, int minLength = DefaultMinTokenLength)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        return WhitespaceAndPunctuationSplitter()
            .Split(text.ToLowerInvariant())
            .Where(t => t.Length >= minLength)
            .ToHashSet();
    }

    /// <summary>
    /// Tokenize text into lowercase words by splitting on whitespace/punctuation.
    /// Returns an array preserving order and duplicates.
    /// Used by SimHash and Jaccard similarity detection.
    /// </summary>
    /// <param name="text">Text to tokenize</param>
    /// <param name="minLength">Minimum token length (default: 2)</param>
    /// <returns>Array of lowercase tokens (may contain duplicates)</returns>
    public static string[] TokenizeToArray(string? text, int minLength = DefaultMinTokenLength)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        return WhitespaceAndPunctuationSplitter()
            .Split(text.ToLowerInvariant())
            .Where(t => t.Length >= minLength)
            .ToArray();
    }

    #endregion

    #region Word Extraction (Bayes, Content Checks)

    /// <summary>
    /// Extract words from text using word boundary detection.
    /// Applies configurable filtering: emojis, stop words, numbers.
    /// Used by Bayes classifier and content detection checks.
    /// </summary>
    /// <param name="text">Text to extract words from</param>
    /// <param name="options">Tokenization options (null = default options)</param>
    /// <returns>Array of extracted words</returns>
    public static string[] ExtractWords(string? text, TokenizerOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        options ??= TokenizerOptions.Default;

        // Apply preprocessing
        if (options.RemoveEmojis)
            text = RemoveEmojis(text);

        if (options.ConvertToLowerCase)
            text = text.ToLowerInvariant();

        // Extract words using word boundary regex
        var matches = WordBoundaryExtractor().Matches(text);
        var words = new List<string>();

        foreach (Match match in matches)
        {
            var word = match.Value;

            if (word.Length < options.MinWordLength)
                continue;

            if (options.RemoveStopWords && IsStopWord(word))
                continue;

            if (options.RemoveNumbers && int.TryParse(word, out _))
                continue;

            words.Add(word);
        }

        return words.ToArray();
    }

    /// <summary>
    /// Get word frequency map from text.
    /// </summary>
    /// <param name="text">Text to analyze</param>
    /// <param name="options">Tokenization options</param>
    /// <returns>Dictionary of word frequencies</returns>
    public static Dictionary<string, int> GetWordFrequencies(string? text, TokenizerOptions? options = null)
    {
        options ??= TokenizerOptions.Default;

        var words = ExtractWords(text, options);
        var frequencies = new Dictionary<string, int>(
            options.ConvertToLowerCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        foreach (var word in words)
        {
            frequencies[word] = frequencies.GetValueOrDefault(word, 0) + 1;
        }

        return frequencies;
    }

    #endregion

    #region Utility Methods

    /// <summary>
    /// Remove emojis from text.
    /// </summary>
    public static string RemoveEmojis(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var result = EmojiPattern().Replace(text, " ");
        result = WhitespaceCleanup().Replace(result, " ");
        return result.Trim();
    }

    /// <summary>
    /// Check if a word is a common stop word.
    /// </summary>
    public static bool IsStopWord(string word)
    {
        return StopWords.Contains(word);
    }

    #endregion
}

/// <summary>
/// Configuration options for word extraction tokenization.
/// </summary>
public class TokenizerOptions
{
    /// <summary>
    /// Default options: remove emojis, stop words, numbers; min length 2; lowercase.
    /// </summary>
    public static readonly TokenizerOptions Default = new();

    /// <summary>Remove emojis before tokenization.</summary>
    public bool RemoveEmojis { get; set; } = true;

    /// <summary>Remove common stop words from results.</summary>
    public bool RemoveStopWords { get; set; } = true;

    /// <summary>Remove pure numeric tokens.</summary>
    public bool RemoveNumbers { get; set; } = true;

    /// <summary>Minimum word length to include.</summary>
    public int MinWordLength { get; set; } = 2;

    /// <summary>Convert all words to lowercase.</summary>
    public bool ConvertToLowerCase { get; set; } = true;
}
