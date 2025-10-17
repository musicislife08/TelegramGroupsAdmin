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
