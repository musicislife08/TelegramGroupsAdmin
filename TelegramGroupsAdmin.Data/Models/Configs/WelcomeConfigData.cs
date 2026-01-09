namespace TelegramGroupsAdmin.Data.Models.Configs;

/// <summary>
/// Data layer representation of WelcomeConfig for EF Core JSON column mapping.
/// Maps to business model via ToModel/ToDto extensions.
/// Note: WelcomeMode enum stored as int (0=DmWelcome, 1=ChatAcceptDeny)
/// </summary>
public class WelcomeConfigData
{
    /// <summary>
    /// Whether welcome system is enabled
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Welcome mode (stored as int, maps to WelcomeMode enum)
    /// </summary>
    public int Mode { get; set; } = 1; // ChatAcceptDeny

    /// <summary>
    /// Timeout in seconds for user response. Stored as double for consistency with other timeout configs.
    /// </summary>
    public double TimeoutSeconds { get; set; }

    /// <summary>
    /// Main welcome message shown to users
    /// </summary>
    public string MainWelcomeMessage { get; set; } = string.Empty;

    /// <summary>
    /// Short teaser message for DM mode
    /// </summary>
    public string DmChatTeaserMessage { get; set; } = string.Empty;

    /// <summary>
    /// Accept button text
    /// </summary>
    public string AcceptButtonText { get; set; } = string.Empty;

    /// <summary>
    /// Deny button text
    /// </summary>
    public string DenyButtonText { get; set; } = string.Empty;

    /// <summary>
    /// DM button text
    /// </summary>
    public string DmButtonText { get; set; } = string.Empty;
}
