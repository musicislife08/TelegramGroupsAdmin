using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// EF Core entity for chat_prompts table
/// Custom prompts per chat for OpenAI spam detection
/// </summary>
[Table("chat_prompts")]
public class ChatPromptRecordDto
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("chat_id")]
    public long ChatId { get; set; }

    [Column("custom_prompt")]
    [Required]
    public string CustomPrompt { get; set; } = string.Empty;

    [Column("enabled")]
    public bool Enabled { get; set; }

    [Column("added_date")]
    public DateTimeOffset AddedDate { get; set; }

    [Column("added_by")]
    public string? AddedBy { get; set; }

    [Column("notes")]
    public string? Notes { get; set; }
}
