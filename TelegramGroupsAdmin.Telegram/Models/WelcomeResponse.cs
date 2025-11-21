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
    string? TimeoutJobId
);
