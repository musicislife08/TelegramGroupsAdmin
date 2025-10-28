using System.Text.RegularExpressions;

namespace TelegramGroupsAdmin.ContentDetection.Services;

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
            frequencies[word] = frequencies.GetValueOrDefault(word, 0) + 1;
        }

        return frequencies;
    }

    [GeneratedRegex(@"\b[\w']+\b", RegexOptions.Compiled)]
    private static partial Regex WordRegex();
}
