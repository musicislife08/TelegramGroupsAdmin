namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// DTO for spam_check_configs table (database DTO)
/// Per-chat configuration for individual spam check algorithms
/// </summary>
public record SpamCheckConfigRecordDto
{
    public long id { get; init; }
    public string chat_id { get; init; } = string.Empty;
    public string check_name { get; init; } = string.Empty;
    public bool enabled { get; init; }
    public int? confidence_threshold { get; init; }
    public string? configuration_json { get; init; }
    public long modified_date { get; init; }
    public string? modified_by { get; init; }
}

public record SpamCheckConfigRecord(
    long Id,
    string ChatId,
    string CheckName,
    bool Enabled,
    int? ConfidenceThreshold,
    string? ConfigurationJson,
    long ModifiedDate,
    string? ModifiedBy
);
