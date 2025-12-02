using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// EF Core entity for web_notifications table
/// Stores in-app notifications for Web Push channel
/// </summary>
[Table("web_notifications")]
public record WebNotificationDto
{
    [Key]
    [Column("id")]
    public long Id { get; init; }

    /// <summary>
    /// User ID (references users table)
    /// </summary>
    [Column("user_id")]
    [MaxLength(450)]
    [Required]
    public string UserId { get; init; } = string.Empty;

    /// <summary>
    /// Notification subject/title
    /// </summary>
    [Column("subject")]
    [Required]
    public string Subject { get; init; } = string.Empty;

    /// <summary>
    /// Notification message body
    /// </summary>
    [Column("message")]
    [Required]
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// Event type as integer (maps to NotificationEventType enum in Core)
    /// </summary>
    [Column("event_type")]
    public int EventType { get; init; }

    /// <summary>
    /// Whether the notification has been read
    /// </summary>
    [Column("is_read")]
    public bool IsRead { get; init; }

    /// <summary>
    /// When the notification was created
    /// </summary>
    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// When the notification was read (null if unread)
    /// </summary>
    [Column("read_at")]
    public DateTimeOffset? ReadAt { get; init; }
}
