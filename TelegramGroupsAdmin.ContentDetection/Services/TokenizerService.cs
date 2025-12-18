using TelegramGroupsAdmin.Core.Utilities;

namespace TelegramGroupsAdmin.ContentDetection.Services;

/// <summary>
/// Tokenizer service for dependency injection.
/// Delegates to centralized TextTokenizer in Core.
/// </summary>
public class TokenizerService : ITokenizerService
{
    /// <inheritdoc />
    public string[] Tokenize(string text) => TextTokenizer.ExtractWords(text);

    /// <inheritdoc />
    public string RemoveEmojis(string text) => TextTokenizer.RemoveEmojis(text);

    /// <inheritdoc />
    public Dictionary<string, int> GetWordFrequencies(string text) => TextTokenizer.GetWordFrequencies(text);

    /// <inheritdoc />
    public bool IsStopWord(string word) => TextTokenizer.IsStopWord(word);
}
