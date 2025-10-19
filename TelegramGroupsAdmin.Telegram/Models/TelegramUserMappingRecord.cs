namespace TelegramGroupsAdmin.Telegram.Models;

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
