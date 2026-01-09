namespace TelegramGroupsAdmin.Data.Models.Configs;

/// <summary>
/// Data layer representation of TranslationConfig for EF Core JSON column mapping.
/// </summary>
public class TranslationConfigData
{
    public bool UseGlobal { get; set; } = true;

    public bool Enabled { get; set; } = true;

    public bool CheckTranslatedContent { get; set; } = true;

    public int MinMessageLength { get; set; } = 20;

    public double LatinScriptThreshold { get; set; } = 0.8;

    public double LanguageDetectionConfidenceThreshold { get; set; } = 0.80;

    public int ConfidenceThreshold { get; set; } = 80;

    public bool WarnNonEnglish { get; set; }

    public string WarningMessage { get; set; } = "This is an English-only chat. Please use English in {chat_name}. You have {warnings_remaining} warnings remaining before removal.";

    public bool AlwaysRun { get; set; }
}
