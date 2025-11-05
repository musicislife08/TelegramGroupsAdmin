namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// List item model for Telegram users table
/// Includes computed stats and counts for quick display
/// </summary>
public class TelegramUserListItem
{
    public long TelegramUserId { get; set; }
    public string? Username { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? UserPhotoPath { get; set; }
    public bool IsTrusted { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }

    // Computed stats
    public int ChatCount { get; set; }
    public int WarningCount { get; set; }
    public int NoteCount { get; set; }
    public bool IsBanned { get; set; }
    public bool HasWarnings { get; set; }
    public bool IsTagged { get; set; }  // Has notes or tags for tracking
    public bool IsAdmin { get; set; }  // Is admin in at least one managed chat

    // Display helpers
    public string DisplayName => !string.IsNullOrEmpty(Username) ? $"@{Username}" : FirstName ?? $"User {TelegramUserId}";
    public TelegramUserStatus Status
    {
        get
        {
            if (IsTrusted) return TelegramUserStatus.Trusted;
            if (IsBanned) return TelegramUserStatus.Banned;
            if (HasWarnings) return TelegramUserStatus.Warned;
            if (IsTagged) return TelegramUserStatus.Tagged;
            return TelegramUserStatus.Clean;
        }
    }
}
