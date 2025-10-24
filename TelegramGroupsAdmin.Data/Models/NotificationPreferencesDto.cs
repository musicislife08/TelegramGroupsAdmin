using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// EF Core entity for notification_preferences table
/// Stores user notification channel preferences and event filters
/// Uses hybrid schema: hard columns for queryable flags, JSONB for flexible channel configs
/// </summary>
[Table("notification_preferences")]
public class NotificationPreferencesDto
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    /// <summary>
    /// User ID (references users table)
    /// </summary>
    [Column("user_id")]
    [MaxLength(450)]
    [Required]
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Enable/disable Telegram DM notifications (queryable flag)
    /// </summary>
    [Column("telegram_dm_enabled")]
    public bool TelegramDmEnabled { get; set; } = true;

    /// <summary>
    /// Enable/disable email notifications (queryable flag)
    /// </summary>
    [Column("email_enabled")]
    public bool EmailEnabled { get; set; } = false;

    /// <summary>
    /// Channel-specific configurations (JSONB for flexibility)
    /// Stored as JSON, no migration needed to add new channels
    /// </summary>
    [Column("channel_configs", TypeName = "jsonb")]
    public string ChannelConfigs { get; set; } = """
        {
            "email": {"address": null, "digest_minutes": 0},
            "telegram": {}
        }
        """;

    /// <summary>
    /// Event type filters - which events to notify about (JSONB)
    /// </summary>
    [Column("event_filters", TypeName = "jsonb")]
    public string EventFilters { get; set; } = """
        {
            "spam_detected": true,
            "spam_auto_deleted": true,
            "user_banned": true,
            "message_reported": true,
            "chat_health_warning": true,
            "backup_failed": true,
            "malware_detected": true
        }
        """;

    /// <summary>
    /// Encrypted secrets for channels (Data Protection encrypted values as JSONB)
    /// Example: {"ntfy_auth_token": "encrypted_value", "pushover_user_key": "encrypted_value"}
    /// </summary>
    [Column("protected_secrets", TypeName = "jsonb")]
    public string ProtectedSecrets { get; set; } = "{}";

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
