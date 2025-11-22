using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// DTO for training data deduplication analysis
/// Represents a single training sample with metadata for duplicate detection
/// </summary>
public class TrainingSampleDto
{
    public long Id { get; set; }
    public long MessageId { get; set; }
    public string MessageText { get; set; } = string.Empty;
    public string? ContentHash { get; set; }
    public bool IsSpam { get; set; }
    public int Confidence { get; set; }
    public string DetectionSource { get; set; } = string.Empty;
    public DateTimeOffset DetectedAt { get; set; }
    public Actor AddedBy { get; set; } = Actor.Unknown;
}

/// <summary>
/// Represents a group of duplicate training samples
/// Used for UI display and batch operations
/// </summary>
public class DuplicateGroup
{
    /// <summary>
    /// Unique key identifying this duplicate group (content hash or text hash)
    /// </summary>
    public string GroupKey { get; set; } = string.Empty;

    /// <summary>
    /// Representative message text (from first sample in group)
    /// </summary>
    public string MessageText { get; set; } = string.Empty;

    /// <summary>
    /// All training samples in this duplicate group
    /// </summary>
    public List<TrainingSampleDto> Samples { get; set; } = [];

    /// <summary>
    /// Similarity score (1.0 = exact match, 0.9-0.99 = very similar, 0.85-0.89 = similar)
    /// </summary>
    public double SimilarityScore { get; set; }

    /// <summary>
    /// Number of duplicate samples in this group
    /// </summary>
    public int DuplicateCount => Samples.Count;

    /// <summary>
    /// Whether all samples have the same is_spam classification
    /// </summary>
    public bool IsSameClass => Samples.Select(s => s.IsSpam).Distinct().Count() == 1;

    /// <summary>
    /// Whether this group contains cross-class conflicts (both spam and ham)
    /// </summary>
    public bool HasCrossClassConflict => !IsSameClass;

    /// <summary>
    /// Average confidence across all samples
    /// </summary>
    public double AverageConfidence => Samples.Any() ? Samples.Average(s => s.Confidence) : 0;

    /// <summary>
    /// Recommended sample to keep (highest confidence + most recent date)
    /// </summary>
    public TrainingSampleDto? RecommendedKeep { get; set; }
}

/// <summary>
/// Result of deduplication analysis with tiered similarity groups
/// </summary>
public class DuplicateAnalysisResult
{
    /// <summary>
    /// Exact duplicates (100% match via content_hash)
    /// </summary>
    public List<DuplicateGroup> ExactDuplicates { get; set; } = [];

    /// <summary>
    /// Very similar samples (95-99% Jaccard similarity)
    /// </summary>
    public List<DuplicateGroup> VerySimilar { get; set; } = [];

    /// <summary>
    /// Similar samples (90-94% Jaccard similarity)
    /// </summary>
    public List<DuplicateGroup> Similar { get; set; } = [];

    /// <summary>
    /// Cross-class conflicts (same message with both spam and ham labels)
    /// Subset of ExactDuplicates with HasCrossClassConflict = true
    /// </summary>
    public List<DuplicateGroup> CrossClassConflicts { get; set; } = [];

    /// <summary>
    /// Total number of duplicate samples across all groups
    /// </summary>
    public int TotalDuplicateSamples =>
        ExactDuplicates.Sum(g => g.DuplicateCount) +
        VerySimilar.Sum(g => g.DuplicateCount) +
        Similar.Sum(g => g.DuplicateCount);

    /// <summary>
    /// Number of samples that would remain after deduplication (keeping 1 per group)
    /// </summary>
    public int SamplesAfterDeduplication =>
        ExactDuplicates.Count + VerySimilar.Count + Similar.Count;

    /// <summary>
    /// Expected reduction in training data size
    /// </summary>
    public int PotentialReduction => TotalDuplicateSamples - SamplesAfterDeduplication;
}
