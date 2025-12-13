using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// Warning entry stored as JSONB in telegram_users.warnings column.
///
/// ARCHITECTURAL DECISION: Warnings stored as JSONB on user table rather than separate table.
///
/// Rationale:
/// - Warnings are always accessed WITH their user (never independently queried across all users at scale)
/// - Collection is bounded (90-day default expiry keeps array small)
/// - Homelab scale (~3k users max, typically &lt;10 warnings per user) makes JSONB efficient
/// - Single query to get user + all moderation state (no JOINs)
/// - Avoids FK constraint issues that complicated the separate table approach
/// - Cross-user aggregation (e.g., "users with warnings") uses WHERE warnings IS NOT NULL, then in-app counting
///
/// Trade-off accepted: Slightly slower cross-user warning aggregation (acceptable at homelab scale)
/// </summary>
public class WarningEntry
{
    /// <summary>When the warning was issued</summary>
    public DateTimeOffset IssuedAt { get; set; }

    /// <summary>When the warning expires (null = never expires)</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>Reason for the warning</summary>
    public string? Reason { get; set; }

    /// <summary>Actor type: "web_user", "telegram_user", or "system"</summary>
    public string ActorType { get; set; } = "system";

    /// <summary>Actor identifier (web user ID, telegram user ID, or system name)</summary>
    public string ActorId { get; set; } = "unknown";

    /// <summary>Context: Chat ID where warning was issued (optional)</summary>
    public long? ChatId { get; set; }

    /// <summary>Context: Message ID that triggered the warning (optional)</summary>
    public long? MessageId { get; set; }
}

/// <summary>
/// Represents a Telegram user tracked across all managed chats.
/// Foundation for: profile photos, trust/whitelist, warnings, impersonation detection.
/// Notes and tags stored in separate tables (admin_notes, user_tags) with full audit trail.
/// </summary>
[Table("telegram_users")]
public class TelegramUserDto
{
    /// <summary>
    /// Telegram user ID (unique across all of Telegram)
    /// </summary>
    [Key]
    [Column("telegram_user_id")]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public long TelegramUserId { get; set; }

    /// <summary>
    /// Telegram username (without @, nullable - not all users have usernames)
    /// </summary>
    [Column("username")]
    [MaxLength(32)]
    public string? Username { get; set; }

    /// <summary>
    /// User's first name from Telegram profile (nullable for defensive programming)
    /// </summary>
    [Column("first_name")]
    [MaxLength(64)]
    public string? FirstName { get; set; }

    /// <summary>
    /// User's last name from Telegram profile (nullable)
    /// </summary>
    [Column("last_name")]
    [MaxLength(64)]
    public string? LastName { get; set; }

    /// <summary>
    /// Path to downloaded user profile photo (e.g., "user_photos/1312830442.jpg")
    /// Immediate need: Centralized photo storage for UI rendering
    /// </summary>
    [Column("user_photo_path")]
    [MaxLength(255)]
    public string? UserPhotoPath { get; set; }

    /// <summary>
    /// Perceptual hash (pHash) of user's profile photo for impersonation detection
    /// Phase 4.10: Anti-Impersonation Detection (compare photo similarity)
    /// </summary>
    [Column("photo_hash")]
    [MaxLength(64)]
    public string? PhotoHash { get; set; }

    /// <summary>
    /// Telegram's stable file_unique_id for profile photo (detects photo changes)
    /// Used to invalidate cache when user changes their profile picture
    /// Null if user has no profile photo
    /// </summary>
    [Column("photo_file_unique_id")]
    [MaxLength(128)]
    public string? PhotoFileUniqueId { get; set; }

    /// <summary>
    /// Whether this user is a bot (from Telegram API User.IsBot flag)
    /// Phase 1: Bot message tracking - used to filter bots from user statistics
    /// </summary>
    [Column("is_bot")]
    public bool IsBot { get; set; } = false;

    /// <summary>
    /// Whether user is trusted/whitelisted (bypasses spam checks)
    /// Phase 5.5: Auto-trust feature (7 days + 50 clean messages + 0 warnings)
    /// </summary>
    [Column("is_trusted")]
    public bool IsTrusted { get; set; } = false;

    /// <summary>
    /// Whether user is currently banned from all managed chats.
    ///
    /// ARCHITECTURAL DECISION: Ban state stored on user table, not derived from audit log.
    /// - Source of truth for "is user banned?" is this flag (not the user_actions table)
    /// - user_actions table is append-only audit log (historical record of what happened)
    /// - Audit log should never be modified - this flag is mutable state
    /// - Same pattern as is_trusted flag
    /// </summary>
    [Column("is_banned")]
    public bool IsBanned { get; set; } = false;

    /// <summary>
    /// When the ban expires (null = permanent ban).
    /// Only meaningful when IsBanned is true.
    /// Background job checks this for auto-unban.
    /// </summary>
    [Column("ban_expires_at")]
    public DateTimeOffset? BanExpiresAt { get; set; }

    /// <summary>
    /// Active warnings as JSONB collection. See WarningEntry for architectural rationale.
    /// Each warning has expiry - expired warnings are filtered in application code.
    /// Null or empty list = no warnings.
    /// </summary>
    [Column("warnings", TypeName = "jsonb")]
    public List<WarningEntry>? Warnings { get; set; }

    /// <summary>
    /// Whether user has started a DM conversation with the bot (enables private notifications)
    /// Set to true when user sends /start to bot in private chat
    /// Set to false when bot gets blocked by user (Forbidden error)
    /// </summary>
    [Column("bot_dm_enabled")]
    public bool BotDmEnabled { get; set; } = false;

    /// <summary>
    /// First time this user was seen in any managed chat
    /// Used for auto-trust eligibility (requires 7 days minimum)
    /// </summary>
    [Column("first_seen_at")]
    public DateTimeOffset FirstSeenAt { get; set; }

    /// <summary>
    /// Last time this user sent a message in any managed chat
    /// </summary>
    [Column("last_seen_at")]
    public DateTimeOffset LastSeenAt { get; set; }

    /// <summary>
    /// Record creation timestamp
    /// </summary>
    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Record last update timestamp
    /// </summary>
    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }
}
