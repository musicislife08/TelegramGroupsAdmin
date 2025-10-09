namespace TelegramGroupsAdmin.Data.Models;

// NOTE: DTOs are PUBLIC (not internal) because repositories live in the SpamDetection project
// which is a separate assembly. This is acceptable as DTOs are just data containers.

// ============================================================================
// Training Samples
// ============================================================================

/// <summary>
/// DTO for Dapper mapping from PostgreSQL (snake_case column names)
/// Public to allow cross-assembly repository usage
/// Using record with init-only setters for Dapper to handle PostgreSQL arrays properly
/// Positional records don't work because Dapper tries to match constructor parameters
/// </summary>
public record TrainingSampleDto
{
    public long id { get; init; }
    public string message_text { get; init; } = string.Empty;
    public bool is_spam { get; init; }
    public long added_date { get; init; }
    public string source { get; init; } = string.Empty;
    public int? confidence_when_added { get; init; }
    public long[]? chat_ids { get; init; }
    public string? added_by { get; init; }
    public int detection_count { get; init; }
    public long? last_detected_date { get; init; }

    public TrainingSample ToTrainingSample() => new TrainingSample(
        Id: id,
        MessageText: message_text,
        IsSpam: is_spam,
        AddedDate: added_date,
        Source: source,
        ConfidenceWhenAdded: confidence_when_added,
        ChatIds: chat_ids ?? Array.Empty<long>(),
        AddedBy: added_by,
        DetectionCount: detection_count,
        LastDetectedDate: last_detected_date
    );
}

/// <summary>
/// Public domain model for training samples
/// </summary>
public record TrainingSample(
    long Id,
    string MessageText,
    bool IsSpam,
    long AddedDate,
    string Source,
    int? ConfidenceWhenAdded,
    long[] ChatIds,
    string? AddedBy,
    int DetectionCount,
    long? LastDetectedDate
);

/// <summary>
/// DTO for training statistics with proper nullable handling for SUM() results
/// Public to allow cross-assembly repository usage
/// </summary>
public record TrainingStatsDto
{
    public int total { get; init; }
    public int spam { get; init; }
    public int ham { get; init; }

    public TrainingStats ToTrainingStats() => new TrainingStats(
        TotalSamples: total,
        SpamSamples: spam,
        HamSamples: ham,
        SpamPercentage: total > 0 ? (double)spam / total * 100 : 0,
        SamplesBySource: new Dictionary<string, int>()
    );
}

/// <summary>
/// DTO for source counts
/// Public to allow cross-assembly repository usage
/// </summary>
public record SourceCountDto
{
    public string source { get; init; } = string.Empty;
    public long? count { get; init; }

    /// <summary>
    /// Safe count value that defaults to 0 if null
    /// </summary>
    public long SafeCount => count ?? 0;
}

/// <summary>
/// Public domain model for training statistics
/// </summary>
public record TrainingStats(
    int TotalSamples,
    int SpamSamples,
    int HamSamples,
    double SpamPercentage,
    Dictionary<string, int> SamplesBySource
);

// ============================================================================
// Stop Words
// ============================================================================

/// <summary>
/// DTO for Dapper mapping from PostgreSQL (snake_case column names)
/// Public to allow cross-assembly repository usage
/// </summary>
public record StopWordDto
{
    public long id { get; init; }
    public string word { get; init; } = string.Empty;
    public bool enabled { get; init; }
    public long added_date { get; init; }
    public string? added_by { get; init; }
    public string? notes { get; init; }

    public StopWord ToStopWord() => new StopWord(
        Id: id,
        Word: word,
        Enabled: enabled,
        AddedDate: added_date,
        AddedBy: added_by,
        Notes: notes
    );
}

/// <summary>
/// Public domain model for stop words
/// </summary>
public record StopWord(
    long Id,
    string Word,
    bool Enabled,
    long AddedDate,
    string? AddedBy,
    string? Notes
);
