using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// EF Core entity for configs table
/// Unified configuration storage with JSONB columns for different config types
/// chat_id = NULL means global config, otherwise chat-specific override
/// </summary>
[Table("configs")]
public class ConfigRecordDto
{
    /// <summary>
    /// Primary key
    /// </summary>
    [Key]
    [Column("id")]
    public long Id { get; set; }

    /// <summary>
    /// Chat ID (NULL = global config, otherwise chat-specific override)
    /// </summary>
    [Column("chat_id")]
    public long? ChatId { get; set; }

    /// <summary>
    /// Spam detection configuration (JSONB)
    /// </summary>
    [Column("spam_detection_config", TypeName = "jsonb")]
    public string? SpamDetectionConfig { get; set; }

    /// <summary>
    /// Welcome message configuration (JSONB)
    /// </summary>
    [Column("welcome_config", TypeName = "jsonb")]
    public string? WelcomeConfig { get; set; }

    /// <summary>
    /// Log level configuration (JSONB)
    /// </summary>
    [Column("log_config", TypeName = "jsonb")]
    public string? LogConfig { get; set; }

    /// <summary>
    /// Moderation configuration (JSONB) - future use
    /// </summary>
    [Column("moderation_config", TypeName = "jsonb")]
    public string? ModerationConfig { get; set; }

    /// <summary>
    /// Bot protection configuration (JSONB)
    /// Phase 6.1: Bot Auto-Ban
    /// </summary>
    [Column("bot_protection_config", TypeName = "jsonb")]
    public string? BotProtectionConfig { get; set; }

    /// <summary>
    /// File scanning configuration (JSONB)
    /// Phase 4.17: File Scanning (ClamAV, YARA, cloud services)
    /// </summary>
    [Column("file_scanning_config", TypeName = "jsonb")]
    public string? FileScanningConfig { get; set; }

    /// <summary>
    /// Cached permanent invite link for this chat (NULL for global config or public chats)
    /// Used for return-to-chat buttons in DM notifications (welcome, tempban, etc.)
    /// Automatically created/reused by ChatInviteLinkService
    /// </summary>
    [Column("invite_link")]
    public string? InviteLink { get; set; }

    /// <summary>
    /// When this config was created (UTC timestamp)
    /// </summary>
    [Column("created_at")]
    [Required]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// When this config was last updated (UTC timestamp)
    /// </summary>
    [Column("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }
}
