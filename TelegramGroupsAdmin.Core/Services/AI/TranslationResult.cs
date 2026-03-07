namespace TelegramGroupsAdmin.Core.Services.AI;

/// <summary>
/// Result of translation attempt
/// </summary>
public record TranslationResult
{
    public string TranslatedText { get; init; } = string.Empty;
    public string DetectedLanguage { get; init; } = string.Empty;
    public bool WasTranslated { get; init; }

    /// <summary>
    /// AI's confidence in the language detection (0.0-1.0). Null if AI didn't return a value.
    /// </summary>
    public decimal? Confidence { get; init; }
}
