using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// EF Core entity for ban_celebration_gifs table
/// Stores GIF files used for ban celebration messages
/// </summary>
[Table("ban_celebration_gifs")]
public class BanCelebrationGifDto
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    /// <summary>
    /// Relative file path within /data/media/ (e.g., "ban-gifs/1.gif")
    /// </summary>
    [Column("file_path")]
    [MaxLength(255)]
    [Required]
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Cached Telegram file_id for instant re-sending (set after first upload to Telegram)
    /// </summary>
    [Column("file_id")]
    [MaxLength(255)]
    public string? FileId { get; set; }

    /// <summary>
    /// Friendly display name for the GIF in the UI
    /// </summary>
    [Column("name")]
    [MaxLength(100)]
    public string? Name { get; set; }

    /// <summary>
    /// Relative path to the thumbnail image (first frame) for preview display
    /// </summary>
    [Column("thumbnail_path")]
    [MaxLength(255)]
    public string? ThumbnailPath { get; set; }

    /// <summary>
    /// Perceptual hash (aHash) for duplicate detection - 64-bit hash stored as 8 bytes
    /// </summary>
    [Column("photo_hash")]
    [MaxLength(8)]
    public byte[]? PhotoHash { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }
}
