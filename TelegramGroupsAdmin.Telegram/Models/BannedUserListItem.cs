namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Banned user list item with ban-specific details
/// Used in the Banned tab to show ban context
/// </summary>
public class BannedUserListItem
{
    public long TelegramUserId { get; set; }
    public string? Username { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? UserPhotoPath { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public int WarningCount { get; set; }

    // Ban-specific details (from user_actions JOIN)
    public DateTimeOffset BanDate { get; set; }
    public string? BannedBy { get; set; }
    public string? BanReason { get; set; }
    public DateTimeOffset? BanExpires { get; set; }
    public long? TriggerMessageId { get; set; }

    // Display helpers
    public string DisplayName => !string.IsNullOrEmpty(Username) ? $"@{Username}" : FirstName ?? $"User {TelegramUserId}";
    public bool IsPermanentBan => BanExpires == null;
}
