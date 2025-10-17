using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramGroupsAdmin.Data.Models;

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
    /// Whether user is trusted/whitelisted (bypasses spam checks)
    /// Phase 5.5: Auto-trust feature (7 days + 50 clean messages + 0 warnings)
    /// </summary>
    [Column("is_trusted")]
    public bool IsTrusted { get; set; } = false;

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
