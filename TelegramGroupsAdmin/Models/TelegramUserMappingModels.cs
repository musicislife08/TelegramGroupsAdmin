namespace TelegramGroupsAdmin.Models;

/// <summary>
/// UI model for Telegram user mappings
/// </summary>
public record TelegramUserMappingRecord(
    long Id,
    long TelegramId,
    string? TelegramUsername,
    string UserId,
    DateTimeOffset LinkedAt,
    bool IsActive
);

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
