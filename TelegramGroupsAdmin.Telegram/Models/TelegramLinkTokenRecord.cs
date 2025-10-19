namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// UI model for Telegram link tokens
/// </summary>
public record TelegramLinkTokenRecord(
    string Token,
    string UserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? UsedAt,
    long? UsedByTelegramId
);
