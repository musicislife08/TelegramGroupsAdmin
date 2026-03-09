using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Detailed user view with full stats and related data
/// </summary>
public class TelegramUserDetail
{
    // Base user data
    public UserIdentity User { get; set; } = UserIdentity.FromId(0);
    public string? UserPhotoPath { get; set; }
    public string? PhotoHash { get; set; }
    public bool IsTrusted { get; set; }
    public bool BotDmEnabled { get; set; }
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }

    // Moderation state (source of truth from telegram_users table)
    public bool IsBanned { get; set; }
    public DateTimeOffset? BanExpiresAt { get; set; }
    public int KickCount { get; set; }
    public List<WarningEntry> Warnings { get; set; } = [];  // JSONB from telegram_users

    // Profile scan data
    public string? Bio { get; set; }
    public long? PersonalChannelId { get; set; }
    public string? PersonalChannelTitle { get; set; }
    public string? PersonalChannelAbout { get; set; }
    public bool HasPinnedStories { get; set; }
    public string? PinnedStoryCaptions { get; set; }
    public bool IsScam { get; set; }
    public bool IsFake { get; set; }
    public bool IsVerified { get; set; }
    public bool ProfileScanExcluded { get; set; }
    public DateTimeOffset? ProfileScannedAt { get; set; }
    public decimal? ProfileScanScore { get; set; }
    public string? LatestAiReason { get; set; }
    public string? LatestAiSignals { get; set; }

    // Related data
    public List<UserChatMembership> ChatMemberships { get; set; } = [];
    public List<UserActionRecord> Actions { get; set; } = [];  // Audit history (read-only)
    public List<DetectionResultRecord> DetectionHistory { get; set; } = [];
    public List<AdminNote> Notes { get; set; } = [];  // Phase 4.12
    public List<UserTag> Tags { get; set; } = [];  // Phase 4.12

    // Display helpers
    public string DisplayName => User.DisplayName;
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
