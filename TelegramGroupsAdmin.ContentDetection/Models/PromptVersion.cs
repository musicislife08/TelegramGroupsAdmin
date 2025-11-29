namespace TelegramGroupsAdmin.ContentDetection.Models;

/// <summary>
/// UI model for OpenAI custom prompt versions
/// Phase 4.X: Prompt builder with versioning and rollback
/// </summary>
public class PromptVersion
{
    public long Id { get; set; }
    public long ChatId { get; set; }
    public int Version { get; set; }
    public string PromptText { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? GenerationMetadata { get; set; }
}
