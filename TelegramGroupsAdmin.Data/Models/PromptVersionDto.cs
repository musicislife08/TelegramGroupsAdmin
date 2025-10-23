using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// EF Core entity for prompt_versions table
/// Stores versioned custom OpenAI prompts for spam detection per chat
/// Enables rollback and prompt history tracking
/// </summary>
[Table("prompt_versions")]
public class PromptVersionDto
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    /// <summary>
    /// Chat ID this prompt belongs to (0 = global)
    /// </summary>
    [Column("chat_id")]
    public long ChatId { get; set; }

    /// <summary>
    /// Version number (auto-incremented per chat)
    /// </summary>
    [Column("version")]
    public int Version { get; set; }

    /// <summary>
    /// The custom prompt text (replaces default rules section)
    /// </summary>
    [Column("prompt_text")]
    public string PromptText { get; set; } = string.Empty;

    /// <summary>
    /// Whether this is the currently active version
    /// </summary>
    [Column("is_active")]
    public bool IsActive { get; set; }

    /// <summary>
    /// Timestamp when this version was created
    /// </summary>
    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Web user ID who created this prompt version
    /// </summary>
    [Column("created_by")]
    [MaxLength(450)]
    public string? CreatedBy { get; set; }

    /// <summary>
    /// JSON metadata about how this prompt was generated
    /// Stores: topic, description, rules, strictness, etc.
    /// </summary>
    [Column("generation_metadata", TypeName = "jsonb")]
    public string? GenerationMetadata { get; set; }
}
