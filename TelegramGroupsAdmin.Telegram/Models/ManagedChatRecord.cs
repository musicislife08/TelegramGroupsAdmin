using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Managed chat record for UI display
/// </summary>
public record ManagedChatRecord(
    ChatIdentity Identity,
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
