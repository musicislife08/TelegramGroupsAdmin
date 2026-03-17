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
}
