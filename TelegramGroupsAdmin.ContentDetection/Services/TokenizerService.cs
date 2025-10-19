using System.Text;
using System.Text.RegularExpressions;

namespace TelegramGroupsAdmin.ContentDetection.Services;

/// <summary>
/// Shared tokenizer service used by Similarity, Bayes, and other checks.
/// Provides consistent text preprocessing with emoji removal and word splitting.
/// </summary>
public interface ITokenizerService
{
    /// <summary>
    /// Tokenize text into words with preprocessing
    /// </summary>
    string[] Tokenize(string text);

    /// <summary>
    /// Remove emojis from text
    /// </summary>
    string RemoveEmojis(string text);

    /// <summary>
    /// Get word frequency map for text
    /// </summary>
    Dictionary<string, int> GetWordFrequencies(string text);

    /// <summary>
    /// Check if word is a stop word
    /// </summary>
    bool IsStopWord(string word);
}

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
        result = Regex.Replace(result, @"\s+", " ");

        return result.Trim();
    }

    /// <summary>
    /// Tokenize text into words with preprocessing
    /// </summary>
    public string[] Tokenize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<string>();

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
            if (frequencies.ContainsKey(word))
                frequencies[word]++;
            else
                frequencies[word] = 1;
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

/// <summary>
/// Tokenizer options for different use cases
/// </summary>
public class TokenizerOptions
{
    public bool RemoveEmojis { get; set; } = true;
    public bool RemoveStopWords { get; set; } = true;
    public bool RemoveNumbers { get; set; } = true;
    public int MinWordLength { get; set; } = 2;
    public bool ConvertToLowerCase { get; set; } = true;
}

/// <summary>
/// Advanced tokenizer with configurable options
/// </summary>
public partial class AdvancedTokenizerService : ITokenizerService
{
    private readonly TokenizerOptions _options;
    private readonly TokenizerService _baseTokenizer;

    public AdvancedTokenizerService(TokenizerOptions? options = null)
    {
        _options = options ?? new TokenizerOptions();
        _baseTokenizer = new TokenizerService();
    }

    public string RemoveEmojis(string text) => _baseTokenizer.RemoveEmojis(text);

    public bool IsStopWord(string word) => _baseTokenizer.IsStopWord(word);

    public string[] Tokenize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<string>();

        // Apply preprocessing based on options
        if (_options.RemoveEmojis)
            text = RemoveEmojis(text);

        if (_options.ConvertToLowerCase)
            text = text.ToLowerInvariant();

        // Extract words
        var matches = WordRegex().Matches(text);
        var words = new List<string>();

        foreach (Match match in matches)
        {
            var word = match.Value;

            // Apply filters based on options
            if (word.Length < _options.MinWordLength)
                continue;

            if (_options.RemoveStopWords && IsStopWord(word))
                continue;

            if (_options.RemoveNumbers && int.TryParse(word, out _))
                continue;

            words.Add(word);
        }

        return words.ToArray();
    }

    public Dictionary<string, int> GetWordFrequencies(string text)
    {
        var words = Tokenize(text);
        var frequencies = new Dictionary<string, int>(
            _options.ConvertToLowerCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        foreach (var word in words)
        {
            if (frequencies.ContainsKey(word))
                frequencies[word]++;
            else
                frequencies[word] = 1;
        }

        return frequencies;
    }

    [GeneratedRegex(@"\b[\w']+\b", RegexOptions.Compiled)]
    private static partial Regex WordRegex();
}