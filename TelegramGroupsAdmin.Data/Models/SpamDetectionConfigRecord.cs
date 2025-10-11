namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// DTO for spam_detection_configs table (database DTO)
/// Global and per-chat spam detection configuration (stored as JSON)
/// </summary>
public record SpamDetectionConfigRecordDto
{
    public long id { get; init; }
    public string? chat_id { get; init; }
    public string config_json { get; init; } = string.Empty;
    public long last_updated { get; init; }
    public string? updated_by { get; init; }
}

public record SpamDetectionConfigRecord(
    long Id,
    string? ChatId,
    string ConfigJson,
    long LastUpdated,
    string? UpdatedBy
);
