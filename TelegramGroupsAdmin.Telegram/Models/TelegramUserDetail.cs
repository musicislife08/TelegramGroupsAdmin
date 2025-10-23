namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Detailed user view with full stats and related data
/// </summary>
public class TelegramUserDetail
{
    // Base user data
    public long TelegramUserId { get; set; }
    public string? Username { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? UserPhotoPath { get; set; }
    public string? PhotoHash { get; set; }
    public bool IsTrusted { get; set; }
    public bool BotDmEnabled { get; set; }
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }

    // Related data
    public List<UserChatMembership> ChatMemberships { get; set; } = new();
    public List<UserActionRecord> Actions { get; set; } = new();  // Warnings, bans, trusts
    public List<DetectionResultRecord> DetectionHistory { get; set; } = new();
    public List<AdminNote> Notes { get; set; } = new();  // Phase 4.12
    public List<UserTag> Tags { get; set; } = new();  // Phase 4.12

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

    public bool IsBanned => Actions.Any(a => a.ActionType == UserActionType.Ban &&
        (a.ExpiresAt == null || a.ExpiresAt > DateTimeOffset.UtcNow));

    public bool HasWarnings => Actions.Any(a => a.ActionType == UserActionType.Warn &&
        (a.ExpiresAt == null || a.ExpiresAt > DateTimeOffset.UtcNow));

    public bool IsTagged => Notes.Any() || Tags.Any(); // Has notes or tags for tracking
}
