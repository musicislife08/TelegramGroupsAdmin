using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// EF Core entity for spam_check_configs table
/// Per-chat configuration for individual spam check algorithms
/// </summary>
[Table("spam_check_configs")]
public class SpamCheckConfigRecordDto
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("chat_id")]
    public long ChatId { get; set; }

    [Column("check_name")]
    [Required]
    public string CheckName { get; set; } = string.Empty;

    [Column("enabled")]
    public bool Enabled { get; set; }

    [Column("confidence_threshold")]
    public int? ConfidenceThreshold { get; set; }

    [Column("configuration_json")]
    public string? ConfigurationJson { get; set; }

    [Column("modified_date")]
    public DateTimeOffset ModifiedDate { get; set; }

    [Column("modified_by")]
    public string? ModifiedBy { get; set; }
}
