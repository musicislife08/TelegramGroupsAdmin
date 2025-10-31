namespace TelegramGroupsAdmin.Data.Models;

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

/// <summary>
/// Training sample for image spam detection ML model
/// Stores labeled images (spam/ham) for training and hash similarity matching
/// </summary>
[Table("image_training_samples")]
public class ImageTrainingSampleDto
{
    /// <summary>
    /// Primary key
    /// </summary>
    [Key]
    [Column("id")]
    public long Id { get; set; }

    /// <summary>
    /// Reference to the message containing this image
    /// </summary>
    [Column("message_id")]
    public long MessageId { get; set; }

    /// <summary>
    /// Path to the image file on disk (/data/media/...)
    /// </summary>
    [Column("photo_path")]
    [Required]
    public string PhotoPath { get; set; } = string.Empty;

    /// <summary>
    /// Perceptual hash (64-bit aHash) for similarity matching
    /// </summary>
    [Column("photo_hash")]
    [Required]
    public byte[] PhotoHash { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// File size in bytes (metadata feature)
    /// </summary>
    [Column("file_size_bytes")]
    public int FileSizeBytes { get; set; }

    /// <summary>
    /// Image width in pixels (metadata feature)
    /// </summary>
    [Column("width")]
    public int Width { get; set; }

    /// <summary>
    /// Image height in pixels (metadata feature)
    /// </summary>
    [Column("height")]
    public int Height { get; set; }

    /// <summary>
    /// Label: true = spam, false = ham (not spam)
    /// </summary>
    [Column("is_spam")]
    public bool IsSpam { get; set; }

    // Exclusive Arc actor system (Phase 4.19) - exactly one must be non-null
    /// <summary>
    /// Web user who labeled this image (from UI)
    /// </summary>
    [Column("marked_by_web_user_id")]
    [MaxLength(450)]
    public string? MarkedByWebUserId { get; set; }

    /// <summary>
    /// Telegram user who labeled this image (from bot command)
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
    /// When this image was labeled
    /// </summary>
    [Column("marked_at")]
    public DateTimeOffset MarkedAt { get; set; }

    // Navigation properties
    public MessageRecordDto? Message { get; set; }
}
