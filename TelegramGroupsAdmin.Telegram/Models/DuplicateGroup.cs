namespace TelegramGroupsAdmin.Telegram.Models;

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
    /// Average score across all samples
    /// </summary>
    public double AverageScore => Samples.Any() ? Samples.Average(s => s.Score) : 0;

    /// <summary>
    /// Recommended sample to keep (highest confidence + most recent date)
    /// </summary>
    public TrainingSampleDto? RecommendedKeep { get; set; }
}
