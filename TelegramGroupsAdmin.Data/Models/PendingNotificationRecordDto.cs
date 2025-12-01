using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// Stores notifications that failed to deliver (e.g., DM blocked) for later retry when user enables DMs
/// </summary>
[Table("pending_notifications")]
public class PendingNotificationRecordDto
{
    /// <summary>
    /// Unique identifier for this pending notification
    /// </summary>
    [Key]
    [Column("id")]
    public long Id { get; set; }

    /// <summary>
    /// Telegram user ID this notification is for
    /// </summary>
    [Required]
    [Column("telegram_user_id")]
    public long TelegramUserId { get; set; }

    /// <summary>
    /// Notification type (e.g., "warning", "mystatus", "welcome")
    /// </summary>
    [Required]
    [Column("notification_type")]
    [MaxLength(50)]
    public string NotificationType { get; set; } = string.Empty;

    /// <summary>
    /// The formatted message text to send
    /// </summary>
    [Required]
    [Column("message_text")]
    public string MessageText { get; set; } = string.Empty;

    /// <summary>
    /// When this notification was first created and failed to deliver
    /// </summary>
    [Required]
    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Number of times we've attempted to deliver this notification
    /// </summary>
    [Required]
    [Column("retry_count")]
    public int RetryCount { get; set; } = 0;

    /// <summary>
    /// When this notification should expire and be discarded (default 30 days)
    /// </summary>
    [Required]
    [Column("expires_at")]
    public DateTimeOffset ExpiresAt { get; set; }
}
