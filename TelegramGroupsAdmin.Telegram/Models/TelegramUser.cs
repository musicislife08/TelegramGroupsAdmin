namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Represents a Telegram user tracked across all managed chats.
/// Foundation for: profile photos, trust/whitelist, warnings, impersonation detection.
/// </summary>
public record TelegramUser(
    long TelegramUserId,
    string? Username,
    string? FirstName,
    string? LastName,
    string? UserPhotoPath,
    string? PhotoHash,
    string? PhotoFileUniqueId,
    bool IsTrusted,
    bool BotDmEnabled,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);
