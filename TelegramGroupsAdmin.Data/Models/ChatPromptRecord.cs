namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// DTO for chat_prompts table (database DTO)
/// Custom prompts per chat for OpenAI spam detection
/// </summary>
public record ChatPromptRecordDto
{
    public long id { get; init; }
    public string chat_id { get; init; } = string.Empty;
    public string custom_prompt { get; init; } = string.Empty;
    public bool enabled { get; init; }
    public long added_date { get; init; }
    public string? added_by { get; init; }
    public string? notes { get; init; }
}

public record ChatPromptRecord(
    long Id,
    string ChatId,
    string CustomPrompt,
    bool Enabled,
    long AddedDate,
    string? AddedBy,
    string? Notes
);
