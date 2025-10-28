namespace TelegramGroupsAdmin.ContentDetection.Configuration;

/// <summary>
/// Foreign language translation configuration
/// </summary>
public class TranslationConfig
{
    /// <summary>
    /// Whether to use global configuration instead of chat-specific overrides
    /// Always true for global config (chat_id=0), can be true/false for chat configs
    /// </summary>
    public bool UseGlobal { get; set; } = true;

    /// <summary>
    /// Whether translation is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Whether to run spam checks on translated content
    /// </summary>
    public bool CheckTranslatedContent { get; set; } = true;

    /// <summary>
    /// Minimum message length to trigger translation (characters)
    /// Messages shorter than this skip expensive OpenAI translation
    /// </summary>
    public int MinMessageLength { get; set; } = 20;

    /// <summary>
    /// Latin script threshold for skipping translation (0.0 - 1.0)
    /// If >= this ratio of characters are Latin script, skip expensive translation
    /// Default: 0.8 (80% Latin = likely English/Western European language)
    /// </summary>
    public double LatinScriptThreshold { get; set; } = 0.8;

    /// <summary>
    /// Confidence threshold for spam classification (0-100)
    /// </summary>
    public int ConfidenceThreshold { get; set; } = 80;

    /// <summary>
    /// Enable language warnings for non-English messages (Phase 4.21)
    /// When enabled, untrusted users posting non-spam messages in non-English will receive a warning
    /// </summary>
    public bool WarnNonEnglish { get; set; } = false;

    /// <summary>
    /// Warning message template sent to users (translated to their language) (Phase 4.21)
    /// Variables: {chat_name}, {language}, {warnings_remaining}
    /// </summary>
    public string WarningMessage { get; set; } = "This is an English-only chat. Please use English in {chat_name}. You have {warnings_remaining} warnings remaining before removal.";
}
