namespace TelegramGroupsAdmin.Telegram.Handlers;

/// <summary>
/// Interface for translation detection and processing.
/// Handles non-English message translation using a three-tier strategy:
/// 1. Latin script ratio for obvious non-Latin (Cyrillic, Chinese, Arabic)
/// 2. FastText language detection for Latin-script languages
/// 3. OpenAI translation for all non-English or low-confidence detections
/// </summary>
public interface ITranslationHandler
{
    /// <summary>
    /// Process message for translation: check eligibility and translate if needed.
    /// Returns translation result if message was translated, null otherwise.
    /// </summary>
    /// <param name="text">The message text to potentially translate.</param>
    /// <param name="messageId">The message ID for logging purposes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Translation result if translated, null otherwise.</returns>
    Task<TranslationProcessingResult?> ProcessTranslationAsync(
        string text,
        int messageId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get text ready for content detection, translating if needed.
    /// Shared by MessageProcessingService and ContentTester for DRY.
    /// </summary>
    /// <param name="text">The original message text.</param>
    /// <param name="messageId">The message ID for logging purposes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result containing text for detection and optional translation metadata.</returns>
    Task<TranslationForDetectionResult> GetTextForDetectionAsync(
        string? text,
        int messageId,
        CancellationToken cancellationToken = default);
}
