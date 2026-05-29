namespace TelegramGroupsAdmin.AI.Services;

/// <summary>
/// Service for translating text using AI for language analysis
/// Supports multiple AI providers via IChatService
/// </summary>
public interface IAITranslationService
{
    /// <summary>
    /// Translate text to English if it's in a foreign language
    /// Returns null if text is already in English, translation fails, or AI not configured
    /// </summary>
    Task<TranslationResult?> TranslateToEnglishAsync(string text, CancellationToken cancellationToken = default);
}
