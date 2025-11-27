using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Service for analyzing and deduplicating training data
/// Identifies exact duplicates, similar messages, and cross-class conflicts
/// </summary>
public class TrainingDataDeduplicationService(
    IDetectionResultsRepository detectionResultsRepository,
    TextSimilarityService similarityService,
    ILogger<TrainingDataDeduplicationService> logger)
{
    /// <summary>
    /// Minimum similarity threshold for "Very Similar" classification (95%)
    /// </summary>
    private const double VerySimilarMinThreshold = 0.95;

    /// <summary>
    /// Maximum similarity threshold for "Very Similar" classification (99%)
    /// </summary>
    private const double VerySimilarMaxThreshold = 0.99;

    /// <summary>
    /// Minimum similarity threshold for "Similar" classification (90%)
    /// </summary>
    private const double SimilarMinThreshold = 0.90;

    /// <summary>
    /// Maximum similarity threshold for "Similar" classification (94%)
    /// </summary>
    private const double SimilarMaxThreshold = 0.94;

    /// <summary>
    /// Analyze all training data for duplicates and return tiered results
    /// </summary>
    /// <remarks>
    /// This method loads all training samples into memory for analysis. Designed for homelab scale
    /// (400-5,000 samples). For larger datasets (>10k samples), consider implementing streaming
    /// or database-level similarity search to reduce memory pressure.
    /// </remarks>
    public async Task<DuplicateAnalysisResult> AnalyzeTrainingDataAsync(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Starting training data deduplication analysis");

        var samples = await detectionResultsRepository.GetAllTrainingDataAsync(cancellationToken);

        var trainingSamples = samples
            .Where(s => s.MessageText != null)
            .Select(s => new TrainingSampleDto
            {
                Id = s.Id,
                MessageId = s.MessageId,
                MessageText = s.MessageText ?? string.Empty,
                ContentHash = s.ContentHash,
                IsSpam = s.IsSpam,
                Confidence = s.Confidence,
                DetectionSource = s.DetectionSource,
                DetectedAt = s.DetectedAt,
                AddedBy = s.AddedBy
            })
            .ToList();

        logger.LogInformation("Loaded {Count} training samples for analysis", trainingSamples.Count);

        var result = new DuplicateAnalysisResult();

        var exactDuplicates = FindExactDuplicates(trainingSamples);
        result.ExactDuplicates = exactDuplicates;

        var verySimilar = FindSimilarSamples(trainingSamples, VerySimilarMinThreshold, VerySimilarMaxThreshold);
        result.VerySimilar = verySimilar;

        var similar = FindSimilarSamples(trainingSamples, SimilarMinThreshold, SimilarMaxThreshold);
        result.Similar = similar;

        // Extract cross-class conflicts from exact duplicates
        result.CrossClassConflicts = exactDuplicates
            .Where(g => g.HasCrossClassConflict)
            .ToList();

        logger.LogInformation(
            "Deduplication analysis complete: {ExactCount} exact duplicate groups, {VerySimilarCount} very similar groups, {SimilarCount} similar groups, {ConflictCount} cross-class conflicts",
            result.ExactDuplicates.Count,
            result.VerySimilar.Count,
            result.Similar.Count,
            result.CrossClassConflicts.Count);

        return result;
    }

    /// <summary>
    /// Find exact duplicate groups using content hash when available, falling back to text comparison
    /// Groups samples with identical content and selects recommended sample to keep
    /// </summary>
    private List<DuplicateGroup> FindExactDuplicates(List<TrainingSampleDto> samples)
    {
        // Group by content hash when available (more efficient), otherwise by normalized text
        var groups = samples
            .GroupBy(s => s.ContentHash ?? s.MessageText.Trim().ToLowerInvariant())
            .Where(g => g.Count() > 1) // Only groups with 2+ samples
            .Select(g => new DuplicateGroup
            {
                GroupKey = g.Key,
                MessageText = g.First().MessageText,
                Samples = g.OrderByDescending(s => s.Confidence)
                          .ThenByDescending(s => s.DetectedAt)
                          .ToList(),
                SimilarityScore = 1.0,
                RecommendedKeep = SelectRecommendedSample(g.ToList())
            })
            .OrderByDescending(g => g.DuplicateCount)
            .ToList();

        logger.LogDebug("Found {Count} exact duplicate groups", groups.Count);
        return groups;
    }

    /// <summary>
    /// Find similar samples within a specified Jaccard similarity range
    /// Uses pairwise comparison (O(n²) - acceptable for small datasets)
    /// </summary>
    private List<DuplicateGroup> FindSimilarSamples(
        List<TrainingSampleDto> samples,
        double minSimilarity,
        double maxSimilarity)
    {
        var groups = new List<DuplicateGroup>();
        var processedSamples = new HashSet<long>();

        for (int i = 0; i < samples.Count; i++)
        {
            if (processedSamples.Contains(samples[i].Id))
                continue;

            var similarSamples = new List<TrainingSampleDto> { samples[i] };
            var maxScore = 0.0;

            for (int j = i + 1; j < samples.Count; j++)
            {
                if (processedSamples.Contains(samples[j].Id))
                    continue;

                var similarity = similarityService.CalculateSimilarity(
                    samples[i].MessageText,
                    samples[j].MessageText);

                if (similarity >= minSimilarity && similarity <= maxSimilarity)
                {
                    similarSamples.Add(samples[j]);
                    maxScore = Math.Max(maxScore, similarity);
                }
            }

            // Only create group if we found similar samples (2+ total)
            if (similarSamples.Count > 1)
            {
                foreach (var sample in similarSamples)
                {
                    processedSamples.Add(sample.Id);
                }

                groups.Add(new DuplicateGroup
                {
                    GroupKey = $"similar_{samples[i].Id}",
                    MessageText = samples[i].MessageText,
                    Samples = similarSamples.OrderByDescending(s => s.Confidence)
                                           .ThenByDescending(s => s.DetectedAt)
                                           .ToList(),
                    SimilarityScore = maxScore,
                    RecommendedKeep = SelectRecommendedSample(similarSamples)
                });
            }
        }

        logger.LogDebug(
            "Found {Count} similar groups ({MinSimilarity:P0}-{MaxSimilarity:P0} similarity)",
            groups.Count, minSimilarity, maxSimilarity);

        return groups.OrderByDescending(g => g.DuplicateCount).ToList();
    }

    /// <summary>
    /// Select recommended sample to keep based on priority rules:
    /// 1. Highest confidence
    /// 2. Most recent date (tie-breaker)
    /// Note: Does NOT prioritize manual over auto (per user preference)
    /// </summary>
    private static TrainingSampleDto SelectRecommendedSample(List<TrainingSampleDto> samples)
    {
        return samples
            .OrderByDescending(s => s.Confidence)
            .ThenByDescending(s => s.DetectedAt)
            .First();
    }
}
