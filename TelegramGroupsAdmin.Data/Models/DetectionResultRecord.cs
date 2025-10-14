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
    public bool used_for_training { get; init; } = true;
    public int? net_confidence { get; init; }
    public string? check_results { get; init; }  // Phase 2.6: JSONB stored as string
    public int edit_version { get; init; }       // Phase 2.6: Message version (0 = original, 1+ = edits)

    public DetectionResultRecord ToDetectionResultRecord() => new DetectionResultRecord(
        Id: id,
        MessageId: message_id,
        DetectedAt: detected_at,
        DetectionSource: detection_source,
        DetectionMethod: detection_method,
        IsSpam: is_spam,
        Confidence: confidence,
        Reason: reason,
        AddedBy: added_by,
        UsedForTraining: used_for_training,
        NetConfidence: net_confidence,
        CheckResultsJson: check_results,
        EditVersion: edit_version
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
    string? AddedBy,
    bool UsedForTraining = true,
    int? NetConfidence = null,
    string? CheckResultsJson = null,  // Phase 2.6: JSON string for all individual check results
    int EditVersion = 0                // Phase 2.6: Track message version
);
