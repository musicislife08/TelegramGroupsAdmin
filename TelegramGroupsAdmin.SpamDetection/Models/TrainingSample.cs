namespace TelegramGroupsAdmin.SpamDetection.Models;

/// <summary>
/// Training sample domain model (public API for SpamDetection library)
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
/// Training statistics domain model
/// </summary>
public record TrainingStats(
    int TotalSamples,
    int SpamSamples,
    int HamSamples,
    double SpamPercentage,
    Dictionary<string, int> SamplesBySource
);
