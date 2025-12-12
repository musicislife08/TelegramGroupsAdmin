namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Managed chat record for UI display
/// </summary>
public record ManagedChatRecord(
    long ChatId,
    string? ChatName,
    ManagedChatType ChatType,
    BotChatStatus BotStatus,
    bool IsAdmin,
    DateTimeOffset AddedAt,
    bool IsActive,
    bool IsDeleted,
    DateTimeOffset? LastSeenAt,
    string? SettingsJson,
    string? ChatIconPath
);
