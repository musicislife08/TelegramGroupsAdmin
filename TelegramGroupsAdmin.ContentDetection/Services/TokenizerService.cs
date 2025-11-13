using System.Text.RegularExpressions;

namespace TelegramGroupsAdmin.ContentDetection.Services;

public partial class TokenizerService : ITokenizerService
{
    // Common stop words to filter out (similar to tg-spam's excluded tokens)
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
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

    // Regex for emoji detection - matches most common emoji ranges (simplified for .NET compatibility)
    [GeneratedRegex(@"[\u2600-\u26FF]|[\u2700-\u27BF]", RegexOptions.Compiled)]
    private static partial Regex EmojiRegex();

    // Regex for extracting words (letters and numbers)
    [GeneratedRegex(@"\b[\w']+\b", RegexOptions.Compiled)]
    private static partial Regex WordRegex();

    // Regex for cleaning up multiple whitespace characters
    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex WhitespaceRegex();

    /// <summary>
    /// Remove emojis from text
    /// </summary>
    public string RemoveEmojis(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        // Remove emojis using regex
        var result = EmojiRegex().Replace(text, " ");

        // Clean up multiple spaces
        result = WhitespaceRegex().Replace(result, " ");

        return result.Trim();
    }

    /// <summary>
    /// Tokenize text into words with preprocessing
    /// </summary>
    public string[] Tokenize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        // Remove emojis first
        text = RemoveEmojis(text);

        // Convert to lowercase for consistent processing
        text = text.ToLowerInvariant();

        // Extract words using regex
        var matches = WordRegex().Matches(text);

        var words = new List<string>();
        foreach (Match match in matches)
        {
            var word = match.Value;

            // Skip very short words
            if (word.Length < 2)
                continue;

            // Skip stop words if configured
            if (IsStopWord(word))
                continue;

            // Skip numbers
            if (int.TryParse(word, out _))
                continue;

            words.Add(word);
        }

        return words.ToArray();
    }

    /// <summary>
    /// Get word frequency map for text
    /// </summary>
    public Dictionary<string, int> GetWordFrequencies(string text)
    {
        var words = Tokenize(text);
        var frequencies = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var word in words)
        {
            frequencies[word] = frequencies.GetValueOrDefault(word, 0) + 1;
        }

        return frequencies;
    }

    /// <summary>
    /// Check if word is a stop word
    /// </summary>
    public bool IsStopWord(string word)
    {
        return StopWords.Contains(word);
    }
}