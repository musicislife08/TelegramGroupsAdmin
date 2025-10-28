namespace TelegramGroupsAdmin.ContentDetection.Services;

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
