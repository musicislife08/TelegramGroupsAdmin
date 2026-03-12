using TelegramGroupsAdmin.Core.Utilities;

namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Banned user list item with ban-specific details from entity columns.
/// "Who banned and why" detail is accessible via the UserDetails dialog timeline (user_actions).
/// </summary>
public class BannedUserListItem : IUserDisplayInfo
{
    public long TelegramUserId { get; set; }
    public string? Username { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? UserPhotoPath { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public int WarningCount { get; set; }
    public bool IsTrusted { get; set; }
    public bool IsAdmin { get; set; }
    public bool IsTagged { get; set; }

    // Ban-specific details (from telegram_users entity columns)
    public DateTimeOffset? BannedAt { get; set; }
    public DateTimeOffset? BanExpires { get; set; }

    // Display helpers
    public string DisplayName => TelegramDisplayName.Format(FirstName, LastName, Username, TelegramUserId);
    public bool IsPermanentBan => BanExpires == null;
}
