namespace TelegramGroupsAdmin.ContentDetection.Services;

/// <summary>
/// Service for detecting the language of text content.
/// Used to pre-filter translation requests (skip English, translate non-English).
/// </summary>
public interface ILanguageDetectionService
{
    /// <summary>
    /// Detect the language of the given text.
    /// Returns language code (ISO 639-1, e.g., "en", "nl", "de") and confidence score (0.0-1.0).
    /// Returns null if detection fails or model not loaded.
    /// </summary>
    /// <param name="text">Text to analyze</param>
    /// <returns>Tuple of (languageCode, confidence) or null if detection fails</returns>
    (string languageCode, double confidence)? DetectLanguage(string text);
}
