namespace TelegramGroupsAdmin.Models;

/// <summary>
/// Training sample for UI display
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
/// Training statistics for UI display
/// </summary>
public record TrainingStats(
    int TotalSamples,
    int SpamSamples,
    int HamSamples,
    double SpamPercentage,
    Dictionary<string, int> SamplesBySource
);

/// <summary>

/// <summary>
/// Stop word for UI display
/// </summary>
public record StopWord(
    long Id,
    string Word,
    bool Enabled,
    long AddedDate,
    string? AddedBy,
    string? Notes
);
