using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Handlers;

/// <summary>
/// Result of translation processing (detection + coordination + translation)
/// </summary>
public record TranslationProcessingResult(
    MessageTranslation Translation,
    double LatinScriptRatio,
    bool WasTranslated
);
