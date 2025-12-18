using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// EF Core entity for message_translations table
/// Uses Exclusive Arc pattern (Phase 4.19) - translation belongs to EITHER message OR edit
/// </summary>
[Table("message_translations")]
public class MessageTranslationDto
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    // Exclusive Arc pattern: exactly one must be non-null
    [Column("message_id")]
    public long? MessageId { get; set; }

    [Column("edit_id")]
    public long? EditId { get; set; }

    [Column("translated_text")]
    public string TranslatedText { get; set; } = string.Empty;

    [Column("detected_language")]
    [MaxLength(100)]
    public string DetectedLanguage { get; set; } = string.Empty;

    [Column("confidence")]
    public decimal? Confidence { get; set; }

    [Column("translated_at")]
    public DateTimeOffset TranslatedAt { get; set; }

    /// <summary>
    /// 64-bit SimHash fingerprint for text similarity detection.
    /// Used for O(1) training data deduplication via Hamming distance.
    /// Computed from translated_text.
    /// </summary>
    [Column("similarity_hash")]
    public long? SimilarityHash { get; set; }

    // Navigation properties
    [ForeignKey(nameof(MessageId))]
    public virtual MessageRecordDto? Message { get; set; }

    [ForeignKey(nameof(EditId))]
    public virtual MessageEditRecordDto? MessageEdit { get; set; }
}
