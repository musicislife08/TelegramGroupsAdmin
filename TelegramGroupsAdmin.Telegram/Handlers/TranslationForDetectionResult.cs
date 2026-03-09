using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Handlers;

/// <summary>
/// Result for content detection - provides text to use and optional translation metadata
/// </summary>
public record TranslationForDetectionResult(
    string TextForDetection,
    MessageTranslation? Translation,
    string? DetectedLanguage
);
