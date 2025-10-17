using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// EF Core entity for tag_definitions table
/// Stores predefined tags with display colors
/// </summary>
[Table("tag_definitions")]
public class TagDefinitionDto
{
    [Key]
    [Column("tag_name")]
    [MaxLength(50)]
    public string TagName { get; set; } = string.Empty;  // Primary key, lowercase

    [Column("color")]
    public TagColor Color { get; set; } = TagColor.Primary;

    [Column("usage_count")]
    public int UsageCount { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }
}
