namespace TelegramGroupsAdmin.Telegram.Constants;

/// <summary>
/// Centralized constants for translation detection and processing.
/// </summary>
public static class TranslationConstants
{
    /// <summary>
    /// Minimum Latin script ratio to skip language detection (0.3 or 30%)
    /// Messages with less than 30% Latin characters are assumed to be non-Latin scripts (Cyrillic, Chinese, Arabic)
    /// </summary>
    public const double NonLatinScriptThreshold = 0.3;

    /// <summary>
    /// Latin script Unicode range start - Basic Latin
    /// </summary>
    public const char LatinScriptRangeStart = '\u0000';

    /// <summary>
    /// Latin script Unicode range end - Latin Extended-B
    /// Includes: Basic Latin (0x0000-0x007F), Latin-1 Supplement (0x0080-0x00FF),
    /// Latin Extended-A (0x0100-0x017F), Latin Extended-B (0x0180-0x024F)
    /// </summary>
    public const char LatinScriptRangeEnd = '\u024F';
}
