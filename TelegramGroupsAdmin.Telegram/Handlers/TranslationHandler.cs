using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Models.ContentDetection;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.ContentDetection.Services;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services.AI;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Constants;

namespace TelegramGroupsAdmin.Telegram.Handlers;

/// <summary>
/// Handles translation detection and processing for non-English messages.
/// Uses three-tier strategy:
/// 1. Latin script ratio for obvious non-Latin (Cyrillic, Chinese, Arabic)
/// 2. FastText language detection for Latin-script languages (English vs Dutch/German/French/Spanish)
/// 3. OpenAI translation for all non-English or low-confidence detections
/// </summary>
public class TranslationHandler : ITranslationHandler
{
    private readonly IConfigService _configService;
    private readonly IAITranslationService _translationService;
    private readonly ILanguageDetectionService _languageDetectionService;
    private readonly ILogger<TranslationHandler> _logger;

    public TranslationHandler(
        IConfigService configService,
        IAITranslationService translationService,
        ILanguageDetectionService languageDetectionService,
        ILogger<TranslationHandler> logger)
    {
        _configService = configService;
        _translationService = translationService;
        _languageDetectionService = languageDetectionService;
        _logger = logger;
    }

    /// <summary>
    /// Process message for translation: check eligibility and translate if needed.
    /// Returns translation result if message was translated, null otherwise.
    /// Phase 4.20: Translate BEFORE saving to allow reuse in spam detection.
    /// </summary>
    public async Task<TranslationProcessingResult?> ProcessTranslationAsync(
        string text,
        int messageId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        // Load translation configuration via ConfigService (single entry point for all config)
        var spamConfig = await _configService.GetAsync<ContentDetectionConfig>(ConfigType.ContentDetection, 0)
                        ?? new ContentDetectionConfig();

        // Check if translation is enabled and message meets minimum length
        if (!spamConfig.Translation.Enabled ||
            text.Length < spamConfig.Translation.MinMessageLength)
        {
            return null;
        }

        // Three-tier translation detection strategy:
        // Tier 1: Latin script ratio for obvious non-Latin scripts (Cyrillic, Chinese, Arabic)
        var latinRatio = CalculateLatinScriptRatio(text);

        if (latinRatio < TranslationConstants.NonLatinScriptThreshold)
        {
            // Definitely non-Latin script (e.g., Russian, Chinese, Arabic, Hebrew)
            // Skip language detection, proceed directly to translation
            _logger.LogDebug(
                "Non-Latin script detected ({LatinRatio:P0} Latin), will translate to English",
                latinRatio);
        }
        else
        {
            // Latin script detected (could be English, Dutch, German, French, Spanish, etc.)
            // Tier 2: Use FastText to distinguish English from other Latin-script languages
            var detectionResult = _languageDetectionService.DetectLanguage(text);

            if (detectionResult.HasValue)
            {
                var (languageCode, confidence) = detectionResult.Value;
                var confidenceThreshold = spamConfig.Translation.LanguageDetectionConfidenceThreshold;

                // High confidence English detection - skip translation
                if (confidence >= confidenceThreshold && languageCode == "en")
                {
                    _logger.LogDebug(
                        "FastText: English detected ({Confidence:P0} >= {Threshold:P0}), skipping translation",
                        confidence, confidenceThreshold);
                    return null;
                }

                // Non-English or low confidence - proceed to translation
                _logger.LogDebug(
                    "FastText: {Language} detected ({Confidence:P0}), will translate",
                    languageCode, confidence);
            }
            else
            {
                // FastText detection failed (model not loaded, text too short, etc.)
                _logger.LogDebug("FastText unavailable, falling back to OpenAI");
            }
        }

        // Tier 3: Translate via OpenAI (handles language detection + translation)
        var translationResult = await _translationService.TranslateToEnglishAsync(text, cancellationToken);

        if (translationResult == null || !translationResult.WasTranslated)
        {
            return null;
        }

        _logger.LogInformation(
            "Translated message {MessageId} from {Language} to English",
            messageId,
            translationResult.DetectedLanguage);

        // Build MessageTranslation record for database storage
        var messageTranslation = new MessageTranslation(
            Id: 0, // Will be set by INSERT
            MessageId: messageId,
            EditId: null,
            TranslatedText: translationResult.TranslatedText,
            DetectedLanguage: translationResult.DetectedLanguage,
            Confidence: null, // OpenAI doesn't return confidence for translation
            TranslatedAt: DateTimeOffset.UtcNow
        );

        return new TranslationProcessingResult(
            Translation: messageTranslation,
            LatinScriptRatio: latinRatio,
            WasTranslated: true
        );
    }

    /// <summary>
    /// Get text ready for content detection, translating if needed.
    /// Shared by MessageProcessingService and ContentTester for DRY.
    /// </summary>
    public async Task<TranslationForDetectionResult> GetTextForDetectionAsync(
        string? text,
        int messageId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new TranslationForDetectionResult(text ?? string.Empty, null, null);
        }

        var result = await ProcessTranslationAsync(text, messageId, cancellationToken);

        if (result is { WasTranslated: true })
        {
            return new TranslationForDetectionResult(
                result.Translation.TranslatedText,
                result.Translation,
                result.Translation.DetectedLanguage);
        }

        return new TranslationForDetectionResult(text, null, null);
    }

    /// <summary>
    /// Calculate the ratio of Latin script characters to total characters (0.0 - 1.0).
    /// Used to detect if text is likely English/Western European (skip expensive translation).
    ///
    /// Latin script includes:
    /// - Basic Latin (0x0000-0x007F): A-Z, a-z, 0-9
    /// - Latin-1 Supplement (0x0080-0x00FF): À, É, Ñ, etc.
    /// - Latin Extended-A (0x0100-0x017F): Ā, Ę, Ł, etc.
    /// - Latin Extended-B (0x0180-0x024F): Ș, Ț, Ơ, etc.
    /// </summary>
    public static double CalculateLatinScriptRatio(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0.0;

        var totalChars = 0;
        var latinChars = 0;

        foreach (var c in text)
        {
            // Only count letter/digit characters (skip punctuation, whitespace, emoji)
            if (char.IsLetterOrDigit(c))
            {
                totalChars++;

                // Latin script: Basic Latin (0x0000-0x007F) + Latin-1 Supplement (0x0080-0x00FF) +
                // Latin Extended-A (0x0100-0x017F) + Latin Extended-B (0x0180-0x024F)
                if ((c >= TranslationConstants.LatinScriptRangeStart && c <= TranslationConstants.LatinScriptRangeEnd))
                {
                    latinChars++;
                }
            }
        }

        return totalChars > 0 ? (double)latinChars / totalChars : 0.0;
    }
}

/// <summary>
/// Result of translation processing (detection + coordination + translation)
/// </summary>
public record TranslationProcessingResult(
    MessageTranslation Translation,
    double LatinScriptRatio,
    bool WasTranslated
);

/// <summary>
/// Result for content detection - provides text to use and optional translation metadata
/// </summary>
public record TranslationForDetectionResult(
    string TextForDetection,
    MessageTranslation? Translation,
    string? DetectedLanguage
);
