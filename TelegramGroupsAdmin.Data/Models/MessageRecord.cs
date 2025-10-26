using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// EF Core entity for messages table
/// </summary>
[Table("messages")]
public class MessageRecordDto
{
    [Key]
    [Column("message_id")]
    public long MessageId { get; set; }

    [Column("user_id")]
    public long UserId { get; set; }

    [Column("chat_id")]
    public long ChatId { get; set; }

    [Column("timestamp")]
    public DateTimeOffset Timestamp { get; set; }

    [Column("message_text")]
    public string? MessageText { get; set; }

    [Column("photo_file_id")]
    public string? PhotoFileId { get; set; }

    [Column("photo_file_size")]
    public int? PhotoFileSize { get; set; }

    [Column("urls")]
    public string? Urls { get; set; }

    [Column("edit_date")]
    public DateTimeOffset? EditDate { get; set; }

    [Column("content_hash")]
    [MaxLength(64)]
    public string? ContentHash { get; set; }

    [Column("photo_local_path")]
    public string? PhotoLocalPath { get; set; }

    [Column("photo_thumbnail_path")]
    public string? PhotoThumbnailPath { get; set; }

    [Column("deleted_at")]
    public DateTimeOffset? DeletedAt { get; set; }

    [Column("deletion_source")]
    public string? DeletionSource { get; set; }

    [Column("reply_to_message_id")]
    public long? ReplyToMessageId { get; set; }

    // Media attachment fields (Phase 4.X: Media support for GIF, Video, Audio, Voice, Sticker, VideoNote, Document)
    [Column("media_type")]
    public MediaType? MediaType { get; set; }  // EF Core stores enum as integer in database

    [Column("media_file_id")]
    [MaxLength(200)]
    public string? MediaFileId { get; set; }  // Telegram file ID for re-downloading

    [Column("media_file_size")]
    public long? MediaFileSize { get; set; }  // File size in bytes

    [Column("media_file_name")]
    [MaxLength(500)]
    public string? MediaFileName { get; set; }  // Original file name (especially for documents)

    [Column("media_mime_type")]
    [MaxLength(100)]
    public string? MediaMimeType { get; set; }  // MIME type (e.g., "video/mp4", "audio/ogg", "application/pdf")

    [Column("media_local_path")]
    [MaxLength(500)]
    public string? MediaLocalPath { get; set; }  // Local storage path in /data directory

    [Column("media_duration")]
    public int? MediaDuration { get; set; }  // Duration in seconds (for audio/video files)

    /// <summary>
    /// Reason why spam detection was skipped for this message
    /// 0 = NotSkipped (spam check ran)
    /// 1 = UserTrusted (user is trusted)
    /// 2 = UserAdmin (user is chat admin)
    /// </summary>
    [Column("spam_check_skip_reason")]
    public SpamCheckSkipReason SpamCheckSkipReason { get; set; } = SpamCheckSkipReason.NotSkipped;

    // Navigation properties
    public virtual ICollection<DetectionResultRecordDto> DetectionResults { get; set; } = [];
    public virtual ICollection<MessageEditRecordDto> MessageEdits { get; set; } = [];
    public virtual ICollection<UserActionRecordDto> UserActions { get; set; } = [];
}

/// <summary>
/// EF Core entity for message_edits table
/// </summary>
[Table("message_edits")]
public class MessageEditRecordDto
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("message_id")]
    public long MessageId { get; set; }

    [Column("edit_date")]
    public DateTimeOffset EditDate { get; set; }

    [Column("old_text")]
    public string? OldText { get; set; }

    [Column("new_text")]
    public string? NewText { get; set; }

    [Column("old_content_hash")]
    [MaxLength(64)]
    public string? OldContentHash { get; set; }

    [Column("new_content_hash")]
    [MaxLength(64)]
    public string? NewContentHash { get; set; }

    // Navigation property
    [ForeignKey(nameof(MessageId))]
    public virtual MessageRecordDto? Message { get; set; }
}
