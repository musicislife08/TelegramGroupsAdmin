namespace TelegramGroupsAdmin.Core.Services.AI;

/// <summary>
/// Result of translation attempt
/// </summary>
public record TranslationResult
{
    public string TranslatedText { get; init; } = string.Empty;
    public string DetectedLanguage { get; init; } = string.Empty;
    public bool WasTranslated { get; init; }
}
