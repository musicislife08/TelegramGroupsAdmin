namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Photo message record for UI display
/// </summary>
public record PhotoMessageRecord(
    string FileId,
    string? MessageText,
    DateTimeOffset Timestamp
);
