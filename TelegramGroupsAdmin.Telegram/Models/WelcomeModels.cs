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
/// Configuration for welcome message system
/// Stored in configs table as JSONB
/// </summary>
public class WelcomeConfig
{
    public bool Enabled { get; set; }
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
