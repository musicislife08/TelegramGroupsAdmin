using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Data.Models;

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

    // Moderation state (source of truth from telegram_users table)
    public bool IsBanned { get; set; }
    public DateTimeOffset? BanExpiresAt { get; set; }
    public List<WarningEntry> Warnings { get; set; } = [];  // JSONB from telegram_users

    // Related data
    public List<UserChatMembership> ChatMemberships { get; set; } = [];
    public List<UserActionRecord> Actions { get; set; } = [];  // Audit history (read-only)
    public List<DetectionResultRecord> DetectionHistory { get; set; } = [];
    public List<AdminNote> Notes { get; set; } = [];  // Phase 4.12
    public List<UserTag> Tags { get; set; } = [];  // Phase 4.12

    // Display helpers
    public string DisplayName => TelegramDisplayName.Format(FirstName, LastName, Username, TelegramUserId);
    public TelegramUserStatus Status
    {
        get
        {
            if (IsTrusted) return TelegramUserStatus.Trusted;
            if (IsBanned) return TelegramUserStatus.Banned;
            if (HasActiveWarnings) return TelegramUserStatus.Warned;
            if (IsTagged) return TelegramUserStatus.Tagged;
            return TelegramUserStatus.Clean;
        }
    }

    /// <summary>
    /// Count of active (non-expired) warnings
    /// </summary>
    public int ActiveWarningCount => Warnings.Count(w =>
        w.ExpiresAt == null || w.ExpiresAt > DateTimeOffset.UtcNow);

    public bool HasActiveWarnings => ActiveWarningCount > 0;

    public bool IsTagged => Notes.Any() || Tags.Any(); // Has notes or tags for tracking
}
