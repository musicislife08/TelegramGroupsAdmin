using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// EF Core entity for content_detection_configs table
/// Global and per-chat content detection configuration (stored as JSON)
/// </summary>
[Table("content_detection_configs")]
public class ContentDetectionConfigRecordDto
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("chat_id")]
    public long? ChatId { get; set; }

    [Column("config_json")]
    [Required]
    public string ConfigJson { get; set; } = string.Empty;

    [Column("last_updated")]
    public DateTimeOffset LastUpdated { get; set; }

    [Column("updated_by")]
    public string? UpdatedBy { get; set; }
}
