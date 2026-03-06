using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.ContentDetection.Constants;
using TelegramGroupsAdmin.ContentDetection.Utilities;
using TelegramGroupsAdmin.ContentDetection.Models;

namespace TelegramGroupsAdmin.ContentDetection.ML;

/// <summary>
/// Extracts ML.NET features from detection result check_results_json for threshold optimization.
/// Parses JSONB data and converts to feature vectors for training.
/// </summary>
public class FeatureExtractionService
{
    private readonly ILogger<FeatureExtractionService> _logger;

    public FeatureExtractionService(ILogger<FeatureExtractionService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Extract features from a detection result with check_results_json.
    /// Returns null if JSON is malformed or required data is missing.
    /// </summary>
    public ThresholdOptimizationFeatures? ExtractFeatures(
        long detectionId,
        string? checkResultsJson,
        bool isSpam,
        int messageLength)
    {
        if (string.IsNullOrEmpty(checkResultsJson))
        {
            _logger.LogWarning("Detection {DetectionId} has no check_results_json", detectionId);
            return null;
        }

        try
        {
            var checks = CheckResultsSerializer.Deserialize(checkResultsJson);

            // Build lookup from check name to score
            var algorithmScores = checks.ToDictionary(c => c.CheckName, c => (float)c.Score);

            // Determine if this was vetoed by OpenAI
            var openAICheck = checks.FirstOrDefault(c => c.CheckName == CheckName.OpenAI);
            var hasOpenAIClean = openAICheck != null && !openAICheck.IsSpam;
            var hasOtherSpam = checks.Any(c => c.CheckName != CheckName.OpenAI && c.IsSpam);
            var wasVetoed = hasOpenAIClean && hasOtherSpam;

            // Extract individual algorithm scores
            var features = new ThresholdOptimizationFeatures
            {
                BayesScore = GetScore(algorithmScores, CheckName.Bayes),
                StopWordsScore = GetScore(algorithmScores, CheckName.StopWords),
                SimilarityScore = GetScore(algorithmScores, CheckName.Similarity),
                CasScore = GetScore(algorithmScores, CheckName.CAS),
                SpacingScore = GetScore(algorithmScores, CheckName.Spacing),
                MultiLanguageScore = 0f, // MultiLanguage check no longer exists
                OpenAIScore = GetScore(algorithmScores, CheckName.OpenAI),
                ThreatIntelScore = GetScore(algorithmScores, CheckName.ThreatIntel),
                ImageScore = GetScore(algorithmScores, CheckName.ImageSpam),

                // Aggregate features
                TriggeredCheckCount = checks.Count(c => c.IsSpam),
                AverageScore = algorithmScores.Values.Where(v => v > 0).DefaultIfEmpty(0f).Average(),
                MaxScore = algorithmScores.Values.DefaultIfEmpty(0f).Max(),

                // Message metadata
                MessageLength = messageLength,
                HasUrls = 0f,  // Not used - model trained with this feature always 0
                IsMultiLanguage = 0f, // MultiLanguage check no longer exists

                // Label
                WasVetoed = wasVetoed
            };

            return features;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract features from detection {DetectionId}", detectionId);
            return null;
        }
    }

    /// <summary>
    /// Extract features from a batch of detection results.
    /// Returns only valid feature vectors (null entries are filtered out).
    /// </summary>
    public List<ThresholdOptimizationFeatures> ExtractBatch<T>(IEnumerable<T> detections)
        where T : IDetectionResultData
    {
        var features = new List<ThresholdOptimizationFeatures>();

        foreach (var detection in detections)
        {
            var feature = ExtractFeatures(
                detection.DetectionId,
                detection.CheckResultsJson,
                detection.IsSpam,
                detection.MessageLength);

            if (feature != null)
            {
                features.Add(feature);
            }
        }

        _logger.LogInformation("Extracted {Count} feature vectors from batch", features.Count);
        return features;
    }

    /// <summary>
    /// Interface for detection result data (supports both tuples and classes)
    /// </summary>
    public interface IDetectionResultData
    {
        long DetectionId { get; }
        string? CheckResultsJson { get; }
        bool IsSpam { get; }
        int MessageLength { get; }
    }

    private static float GetScore(
        Dictionary<CheckName, float> scores,
        CheckName checkName)
    {
        return scores.TryGetValue(checkName, out var score) ? score : 0f;
    }
}
