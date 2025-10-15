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
    DateTimeOffset CreatedAt
);

/// <summary>
/// Response types for welcome messages
/// </summary>
public enum WelcomeResponseType
{
    Accepted = 0,
    Denied = 1,
    Timeout = 2,
    Left = 3
}

/// <summary>
/// Configuration for welcome message system
/// Stored in configs table as JSONB
/// </summary>
public record WelcomeConfig(
    bool Enabled,
    int TimeoutSeconds,
    string ChatWelcomeTemplate,
    string DmTemplate,
    string ChatFallbackTemplate,
    string AcceptButtonText,
    string DenyButtonText,
    string RulesText
)
{
    /// <summary>
    /// Default configuration (enabled for testing Phase 4.4)
    /// </summary>
    public static WelcomeConfig Default => new(
        Enabled: true,
        TimeoutSeconds: 60,
        ChatWelcomeTemplate: "👋 Welcome {username}!\n\nTo participate in this chat, please read and accept our rules.\n\n📖 Click \"Read Rules\" below to view them privately, then come back here to accept.",
        DmTemplate: "Welcome to {chat_name}! Here are our rules:\n\n{rules_text}\n\n✅ After reading, return to the chat and click \"Accept Rules\" to start participating!",
        ChatFallbackTemplate: "Thanks for accepting! Here are our rules:\n\n{rules_text}",
        AcceptButtonText: "✅ I Accept",
        DenyButtonText: "❌ Decline",
        RulesText: "1. Be respectful\n2. No spam\n3. Stay on topic"
    );
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
