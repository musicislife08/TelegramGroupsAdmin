namespace TelegramGroupsAdmin.Data.Models;

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

/// <summary>
/// Training sample for video spam detection ML model
/// Stores labeled videos (spam/ham) for training and keyframe hash similarity matching
/// </summary>
[Table("video_training_samples")]
public class VideoTrainingSampleDto
{
    /// <summary>
    /// Primary key
    /// </summary>
    [Key]
    [Column("id")]
    public long Id { get; set; }

    /// <summary>
    /// Reference to the message containing this video
    /// </summary>
    [Column("message_id")]
    public long MessageId { get; set; }

    /// <summary>
    /// Path to the video file on disk (/data/media/...)
    /// </summary>
    [Column("video_path")]
    [Required]
    public string VideoPath { get; set; } = string.Empty;

    /// <summary>
    /// Video duration in seconds
    /// </summary>
    [Column("duration_seconds", TypeName = "decimal(5,2)")]
    public decimal DurationSeconds { get; set; }

    /// <summary>
    /// File size in bytes (metadata feature)
    /// </summary>
    [Column("file_size_bytes")]
    public int FileSizeBytes { get; set; }

    /// <summary>
    /// Video width in pixels (metadata feature)
    /// </summary>
    [Column("width")]
    public int Width { get; set; }

    /// <summary>
    /// Video height in pixels (metadata feature)
    /// </summary>
    [Column("height")]
    public int Height { get; set; }

    /// <summary>
    /// JSON array of perceptual hashes for 2-5 keyframes
    /// Format: [{"position": 0.1, "hash": "base64..."}, {"position": 0.5, "hash": "base64..."}]
    /// </summary>
    [Column("keyframe_hashes", TypeName = "jsonb")]
    [Required]
    public string KeyframeHashes { get; set; } = "[]";

    /// <summary>
    /// Whether video has audio track
    /// </summary>
    [Column("has_audio")]
    public bool HasAudio { get; set; }

    /// <summary>
    /// Label: true = spam, false = ham (not spam)
    /// </summary>
    [Column("is_spam")]
    public bool IsSpam { get; set; }

    // Exclusive Arc actor system (Phase 4.19) - exactly one must be non-null
    /// <summary>
    /// Web user who labeled this video (from UI)
    /// </summary>
    [Column("marked_by_web_user_id")]
    [MaxLength(450)]
    public string? MarkedByWebUserId { get; set; }

    /// <summary>
    /// Telegram user who labeled this video (from bot command)
    /// </summary>
    [Column("marked_by_telegram_user_id")]
    public long? MarkedByTelegramUserId { get; set; }

    /// <summary>
    /// System identifier if auto-labeled (e.g., "migration", "auto-trainer")
    /// </summary>
    [Column("marked_by_system_identifier")]
    [MaxLength(50)]
    public string? MarkedBySystemIdentifier { get; set; }

    /// <summary>
    /// When this video was labeled
    /// </summary>
    [Column("marked_at")]
    public DateTimeOffset MarkedAt { get; set; }

    // Navigation properties
    public MessageRecordDto? Message { get; set; }
}
