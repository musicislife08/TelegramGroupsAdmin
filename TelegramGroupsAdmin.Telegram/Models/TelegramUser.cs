using TelegramGroupsAdmin.Core.Utilities;

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
    bool IsBot,
    bool IsTrusted,
    bool IsBanned,
    bool BotDmEnabled,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool IsActive = true
)
{
    /// <summary>
    /// Formatted display name following Telegram conventions.
    /// Priority: Service Account (chatName) → FullName (First + Last) → Username → User {id}
    /// </summary>
    public string DisplayName => TelegramDisplayName.Format(FirstName, LastName, Username, TelegramUserId);
}
