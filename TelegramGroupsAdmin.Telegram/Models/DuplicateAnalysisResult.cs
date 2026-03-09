namespace TelegramGroupsAdmin.Telegram.Models;

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
