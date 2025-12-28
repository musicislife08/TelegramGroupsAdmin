namespace TelegramGroupsAdmin.Ui.Server.Models.Dialogs;

public class AddTrainingSampleData
{
    public string MessageText { get; set; } = string.Empty;
    public bool IsSpam { get; set; }
    public string Source { get; set; } = string.Empty;
    public int? Confidence { get; set; }

    /// <summary>
    /// Optional translated text (Phase 4.20+: Translation support for manual training samples)
    /// </summary>
    public string? TranslatedText { get; set; }

    /// <summary>
    /// Optional detected language code (e.g., "es", "ru", "fr")
    /// Required if TranslatedText is provided
    /// </summary>
    public string? DetectedLanguage { get; set; }
}
