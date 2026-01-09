using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TelegramGroupsAdmin.Data.Models.Configs;

namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// EF Core entity for content_detection_configs table
/// Global and per-chat content detection configuration (stored as JSONB)
/// </summary>
[Table("content_detection_configs")]
public class ContentDetectionConfigRecordDto
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("chat_id")]
    public long? ChatId { get; set; }

    /// <summary>
    /// Content detection configuration as strongly-typed object.
    /// EF Core maps this via OwnsOne().ToJson() to JSONB column "config_json".
    /// Column attribute required for backup service compatibility (identifies JSONB columns).
    /// </summary>
    [Column("config_json", TypeName = "jsonb")]
    public ContentDetectionConfigData? Config { get; set; }

    [Column("last_updated")]
    public DateTimeOffset LastUpdated { get; set; }

    [Column("updated_by")]
    public string? UpdatedBy { get; set; }
}
