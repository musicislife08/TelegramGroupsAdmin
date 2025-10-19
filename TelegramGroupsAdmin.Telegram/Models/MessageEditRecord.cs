namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Message edit record for UI display
/// </summary>
public record MessageEditRecord(
    long Id,
    long MessageId,
    string? OldText,
    string? NewText,
    DateTimeOffset EditDate,
    string? OldContentHash,
    string? NewContentHash
);
