namespace TelegramGroupsAdmin.Data.Models;

// DTO for DetectionResultRecord (PostgreSQL detection_results table)
//
// CRITICAL: All DTO properties MUST use snake_case to match PostgreSQL column names exactly.
// Dapper uses init-only property setters for materialization.
public record DetectionResultRecordDto
{
    public long id { get; init; }
    public long message_id { get; init; }
    public long detected_at { get; init; }
    public string detection_source { get; init; } = string.Empty;
    public string detection_method { get; init; } = string.Empty;
    public bool is_spam { get; init; }
    public int confidence { get; init; }
    public string? reason { get; init; }
    public string? added_by { get; init; }

    public DetectionResultRecord ToDetectionResultRecord() => new DetectionResultRecord(
        Id: id,
        MessageId: message_id,
        DetectedAt: detected_at,
        DetectionSource: detection_source,
        DetectionMethod: detection_method,
        IsSpam: is_spam,
        Confidence: confidence,
        Reason: reason,
        AddedBy: added_by
    );
}

public record DetectionResultRecord(
    long Id,
    long MessageId,
    long DetectedAt,
    string DetectionSource,
    string DetectionMethod,
    bool IsSpam,
    int Confidence,
    string? Reason,
    string? AddedBy
);
