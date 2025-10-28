namespace TelegramGroupsAdmin.ContentDetection.Services;

/// <summary>
/// Service for translating text using OpenAI for language analysis
/// </summary>
public interface IOpenAITranslationService
{
    /// <summary>
    /// Translate text to English if it's in a foreign language
    /// Returns null if text is already in English or if translation fails
    /// </summary>
    Task<TranslationResult?> TranslateToEnglishAsync(string text, CancellationToken cancellationToken = default);
}