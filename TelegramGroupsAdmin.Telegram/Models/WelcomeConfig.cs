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
    public string ChatWelcomeTemplate { get; set; } = string.Empty;
    public string DmTemplate { get; set; } = string.Empty;
    public string ChatFallbackTemplate { get; set; } = string.Empty;
    public string AcceptButtonText { get; set; } = string.Empty;
    public string DenyButtonText { get; set; } = string.Empty;
    public string RulesText { get; set; } = string.Empty;

    /// <summary>
    /// Default configuration (enabled for testing Phase 4.4)
    /// </summary>
    public static WelcomeConfig Default => new()
    {
        Enabled = true,
        Mode = WelcomeMode.ChatAcceptDeny, // Default to chat mode for backward compatibility
        TimeoutSeconds = 60,
        ChatWelcomeTemplate = """
            👋 Welcome {username}!

            To participate in this chat, please read and accept our rules.

            📖 Click "Read Rules" below, then click the START button to receive the rules privately.
            """,
        DmTemplate = """
            Welcome to {chat_name}! Here are our rules:

            {rules_text}

            ✅ Click "I Accept" below, or return to the chat to accept there.
            """,
        ChatFallbackTemplate = """
            Thanks for accepting! Here are our rules:

            {rules_text}
            """,
        AcceptButtonText = "✅ I Accept",
        DenyButtonText = "❌ Decline",
        RulesText = "1. Be respectful\n2. No spam\n3. Stay on topic"
    };
}
