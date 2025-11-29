namespace TelegramGroupsAdmin.Core.Models;

/// <summary>
/// Message translation for UI display
/// Translation belongs to either original message or specific edit (Exclusive Arc pattern)
/// </summary>
public record MessageTranslation(
    long Id,
    long? MessageId,
    long? EditId,
    string TranslatedText,
    string DetectedLanguage,
    decimal? Confidence,
    DateTimeOffset TranslatedAt
);
