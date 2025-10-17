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
    bool IsTrusted,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

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
    public bool IsFlagged { get; set; }  // Has notes, tags, borderline spam, or reports

    // Display helpers
    public string DisplayName => !string.IsNullOrEmpty(Username) ? $"@{Username}" : FirstName ?? $"User {TelegramUserId}";
    public TelegramUserStatus Status
    {
        get
        {
            if (IsTrusted) return TelegramUserStatus.Trusted;
            if (IsBanned) return TelegramUserStatus.Banned;
            if (HasWarnings) return TelegramUserStatus.Warned;
            if (IsFlagged) return TelegramUserStatus.Flagged;
            return TelegramUserStatus.Clean;
        }
    }
}

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
            if (IsFlagged) return TelegramUserStatus.Flagged;
            return TelegramUserStatus.Clean;
        }
    }

    public bool IsBanned => Actions.Any(a => a.ActionType == UserActionType.Ban &&
        (a.ExpiresAt == null || a.ExpiresAt > DateTimeOffset.UtcNow));

    public bool HasWarnings => Actions.Any(a => a.ActionType == UserActionType.Warn &&
        (a.ExpiresAt == null || a.ExpiresAt > DateTimeOffset.UtcNow));

    public bool IsFlagged => Notes.Any() || Tags.Any(); // Phase 4.12: Has notes or tags
}

/// <summary>
/// Chat membership info for user detail view
/// </summary>
public class UserChatMembership
{
    public long ChatId { get; set; }
    public string? ChatName { get; set; }
    public int MessageCount { get; set; }
    public DateTimeOffset LastActivityAt { get; set; }
    public DateTimeOffset FirstSeenAt { get; set; }
}

/// <summary>
/// Telegram user status badge colors
/// </summary>
public enum TelegramUserStatus
{
    Clean = 0,      // 🔵 No issues
    Flagged = 1,    // 🟡 Needs review
    Warned = 2,     // 🟠 Has warnings
    Banned = 3,     // 🔴 Banned
    Trusted = 4     // 🟢 Explicitly trusted
}

/// <summary>
/// Moderation queue statistics
/// </summary>
public class ModerationQueueStats
{
    public int BannedCount { get; set; }
    public int FlaggedCount { get; set; }
    public int WarnedCount { get; set; }
    public int NotesCount { get; set; }  // Future: Phase 4
}

/// <summary>
/// Top active user for leaderboard
/// </summary>
public class TopActiveUser
{
    public long TelegramUserId { get; set; }
    public string? Username { get; set; }
    public string? FirstName { get; set; }
    public string? UserPhotoPath { get; set; }
    public int MessageCount { get; set; }
    public string DisplayName => !string.IsNullOrEmpty(Username) ? $"@{Username}" : FirstName ?? $"User {TelegramUserId}";
}

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
