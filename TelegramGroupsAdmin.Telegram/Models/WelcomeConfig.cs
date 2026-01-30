namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Configuration for welcome message system
/// Stored in configs table as JSONB
/// </summary>
public class WelcomeConfig
{
    public bool Enabled { get; set; }
    public WelcomeMode Mode { get; set; } = WelcomeMode.ChatAcceptDeny;
    public int TimeoutSeconds { get; set; }

    /// <summary>
    /// Main welcome message shown to users. Used in all modes.
    /// In Chat mode: Posted directly in group chat.
    /// In DM mode: Sent in private DM when user clicks button.
    /// In DM fallback: Posted in chat if DM fails.
    /// Variables: {username}, {chat_name}, {timeout}
    /// </summary>
    public string MainWelcomeMessage { get; set; } = string.Empty;

    /// <summary>
    /// Short teaser message shown in group chat for DM mode only.
    /// Prompts user to click button to open DM and receive main message.
    /// Variables: {username}, {timeout}
    /// </summary>
    public string DmChatTeaserMessage { get; set; } = string.Empty;

    public string AcceptButtonText { get; set; } = string.Empty;
    public string DenyButtonText { get; set; } = string.Empty;

    /// <summary>
    /// Text shown on the button in DM teaser message (DM mode only)
    /// </summary>
    public string DmButtonText { get; set; } = string.Empty;

    /// <summary>
    /// Entrance exam configuration (EntranceExam mode only).
    /// Contains MC questions and/or open-ended question with evaluation criteria.
    /// </summary>
    public ExamConfig? ExamConfig { get; set; }

    /// <summary>
    /// Default configuration
    /// </summary>
    public static WelcomeConfig Default => new()
    {
        Enabled = true,
        Mode = WelcomeMode.ChatAcceptDeny,
        TimeoutSeconds = 60,
        MainWelcomeMessage = """
            👋 Welcome {username} to {chat_name}!

            Please read and accept our community guidelines within {timeout} seconds or you will be temporarily removed.

            📖 Our Guidelines:
            1. Be respectful to all members
            2. No spam or self-promotion
            3. Stay on topic

            Click Accept below to continue participating in the chat.
            """,
        DmChatTeaserMessage = """
            Welcome {username}! Click the button below to read the {chat_name} guidelines.

            You have {timeout} seconds to respond.
            """,
        AcceptButtonText = "✅ I Accept",
        DenyButtonText = "❌ Decline",
        DmButtonText = "📋 Read Guidelines"
    };
}
