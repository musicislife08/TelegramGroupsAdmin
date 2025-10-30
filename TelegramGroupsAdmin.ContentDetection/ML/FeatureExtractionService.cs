using System.Text.Json;
using Microsoft.Extensions.Logging;

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
            var json = JsonDocument.Parse(checkResultsJson);
            var checks = json.RootElement.GetProperty("checks");

            // Parse all algorithm results
            var algorithmScores = new Dictionary<string, (string result, float confidence)>();

            foreach (var check in checks.EnumerateArray())
            {
                var name = check.GetProperty("name").GetString() ?? "";
                var result = check.GetProperty("result").GetString() ?? "";
                var confidence = check.TryGetProperty("conf", out var confProp) ? confProp.GetSingle() : 0f;

                algorithmScores[name] = (result, confidence);
            }

            // Determine if this was vetoed by OpenAI
            var hasOpenAIClean = algorithmScores.TryGetValue("OpenAI", out var openAI) && openAI.result == "clean";
            var hasOtherSpam = algorithmScores.Any(kvp => kvp.Key != "OpenAI" && kvp.Value.result == "spam");
            var wasVetoed = hasOpenAIClean && hasOtherSpam;

            // Extract individual algorithm confidences
            var features = new ThresholdOptimizationFeatures
            {
                BayesConfidence = GetConfidence(algorithmScores, "Bayes"),
                StopWordsConfidence = GetConfidence(algorithmScores, "StopWords"),
                SimilarityConfidence = GetConfidence(algorithmScores, "TF-IDF Similarity"),
                CasConfidence = GetConfidence(algorithmScores, "CAS"),
                SpacingConfidence = GetConfidence(algorithmScores, "Spacing"),
                MultiLanguageConfidence = GetConfidence(algorithmScores, "MultiLanguage"),
                OpenAIConfidence = GetConfidence(algorithmScores, "OpenAI"),
                ThreatIntelConfidence = GetConfidence(algorithmScores, "ThreatIntel"),
                ImageConfidence = GetConfidence(algorithmScores, "Image"),

                // Aggregate features
                TriggeredCheckCount = algorithmScores.Count(kvp => kvp.Value.result == "spam"),
                AverageConfidence = algorithmScores.Values.Where(v => v.confidence > 0).Average(v => v.confidence),
                MaxConfidence = algorithmScores.Values.Max(v => v.confidence),

                // Message metadata
                MessageLength = messageLength,
                HasUrls = 0f,  // TODO: Extract from message metadata if needed
                IsMultiLanguage = algorithmScores.ContainsKey("MultiLanguage") &&
                                  algorithmScores["MultiLanguage"].result == "spam" ? 1f : 0f,

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

    private static float GetConfidence(
        Dictionary<string, (string result, float confidence)> scores,
        string algorithmName)
    {
        return scores.TryGetValue(algorithmName, out var score) ? score.confidence : 0f;
    }
}
