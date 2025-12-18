using TelegramGroupsAdmin.Core.Utilities;

namespace TelegramGroupsAdmin.ContentDetection.Services;

/// <summary>
/// Advanced tokenizer with configurable options.
/// Delegates to centralized TextTokenizer in Core.
/// </summary>
public class AdvancedTokenizerService : ITokenizerService
{
    private readonly TokenizerOptions _options;

    public AdvancedTokenizerService(TokenizerOptions? options = null)
    {
        _options = options ?? TokenizerOptions.Default;
    }

    /// <inheritdoc />
    public string[] Tokenize(string text) => TextTokenizer.ExtractWords(text, _options);

    /// <inheritdoc />
    public string RemoveEmojis(string text) => TextTokenizer.RemoveEmojis(text);

    /// <inheritdoc />
    public Dictionary<string, int> GetWordFrequencies(string text) => TextTokenizer.GetWordFrequencies(text, _options);

    /// <inheritdoc />
    public bool IsStopWord(string word) => TextTokenizer.IsStopWord(word);
}
