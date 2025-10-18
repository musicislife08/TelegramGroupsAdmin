namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// UI model for welcome response record
/// Phase 4.4: Welcome Message System
/// </summary>
public record WelcomeResponse(
    long Id,
    long ChatId,
    long UserId,
    string? Username,
    int WelcomeMessageId,
    WelcomeResponseType Response,
    DateTimeOffset RespondedAt,
    bool DmSent,
    bool DmFallback,
    DateTimeOffset CreatedAt,
    Guid? TimeoutJobId
);

/// <summary>
/// Response types for welcome messages
/// </summary>
public enum WelcomeResponseType
{
    Pending = 0,
    Accepted = 1,
    Denied = 2,
    Timeout = 3,
    Left = 4
}

/// <summary>
/// Welcome message delivery mode
/// </summary>
public enum WelcomeMode
{
    /// <summary>Rules sent via DM with deep link button (clean group chat)</summary>
    DmWelcome = 0,

    /// <summary>Rules shown in chat with Accept/Deny buttons (faster onboarding)</summary>
    ChatAcceptDeny = 1
}

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
        ChatWelcomeTemplate = "👋 Welcome {username}!\n\nTo participate in this chat, please read and accept our rules.\n\n📖 Click \"Read Rules\" below, then click the START button to receive the rules privately.",
        DmTemplate = "Welcome to {chat_name}! Here are our rules:\n\n{rules_text}\n\n✅ Click \"I Accept\" below, or return to the chat to accept there.",
        ChatFallbackTemplate = "Thanks for accepting! Here are our rules:\n\n{rules_text}",
        AcceptButtonText = "✅ I Accept",
        DenyButtonText = "❌ Decline",
        RulesText = "1. Be respectful\n2. No spam\n3. Stay on topic"
    };
}

/// <summary>
/// Analytics data for welcome system
/// </summary>
public record WelcomeStats(
    int TotalResponses,
    int AcceptedCount,
    int DeniedCount,
    int TimeoutCount,
    int LeftCount,
    double AcceptanceRate,
    int DmSuccessCount,
    int DmFallbackCount,
    double DmSuccessRate
);

/// <summary>
/// Configuration for warning system
/// Stored in configs table as JSONB
/// Phase 4.11: Warning/Points System
/// </summary>
public class WarningSystemConfig
{
    /// <summary>
    /// Enable automatic ban after reaching threshold
    /// </summary>
    public bool AutoBanEnabled { get; set; }

    /// <summary>
    /// Number of warnings before auto-ban (0 = disabled)
    /// </summary>
    public int AutoBanThreshold { get; set; }

    /// <summary>
    /// Reason shown when user is auto-banned
    /// Supports {count} placeholder for warning count
    /// </summary>
    public string AutoBanReason { get; set; } = string.Empty;

    /// <summary>
    /// Default configuration
    /// </summary>
    public static WarningSystemConfig Default => new()
    {
        AutoBanEnabled = true,
        AutoBanThreshold = 3,
        AutoBanReason = "Automatic ban after {count} warnings"
    };
}
