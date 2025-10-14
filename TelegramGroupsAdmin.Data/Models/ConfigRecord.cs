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
    /// When this config was created (Unix timestamp)
    /// </summary>
    [Column("created_at")]
    [Required]
    public long CreatedAt { get; set; }

    /// <summary>
    /// When this config was last updated (Unix timestamp)
    /// </summary>
    [Column("updated_at")]
    public long? UpdatedAt { get; set; }
}
