using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.ML;
using TelegramGroupsAdmin.ContentDetection.Configuration;
using TelegramGroupsAdmin.ContentDetection.Constants;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.ContentDetection.Utilities;
using TelegramGroupsAdmin.Data;

namespace TelegramGroupsAdmin.ContentDetection.ML;

/// <summary>
/// Generates threshold optimization recommendations using ML.NET binary classification.
/// Trains a model to predict veto probability, then simulates different thresholds to find optimal values.
/// Returns DTOs that can be persisted by the caller.
/// </summary>
public class ThresholdRecommendationService : IThresholdRecommendationService
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ISpamDetectionConfigRepository _configRepository;
    private readonly FeatureExtractionService _featureExtraction;
    private readonly ILogger<ThresholdRecommendationService> _logger;

    // Algorithm names as they appear in check_results_json
    private static readonly string[] KnownAlgorithms =
    [
        "Bayes",
        "StopWords",
        "TF-IDF Similarity",
        "CAS",
        "Spacing",
        "MultiLanguage",
        "ThreatIntel",
        "Image"
    ];

    public ThresholdRecommendationService(
        IDbContextFactory<AppDbContext> contextFactory,
        ISpamDetectionConfigRepository configRepository,
        FeatureExtractionService featureExtraction,
        ILogger<ThresholdRecommendationService> logger)
    {
        _contextFactory = contextFactory;
        _configRepository = configRepository;
        _featureExtraction = featureExtraction;
        _logger = logger;
    }

    public async Task<List<ThresholdRecommendationDto>> GenerateRecommendationsAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting ML.NET threshold recommendation generation for period {Since} to {Now}",
            since, DateTimeOffset.UtcNow);

        // Step 1: Load detection results with check_results_json
        var trainingData = await LoadTrainingDataAsync(since, cancellationToken);

        if (trainingData.Count < 50)
        {
            _logger.LogWarning("Insufficient training data ({Count} samples). Need at least 50.", trainingData.Count);
            return [];
        }

        _logger.LogInformation("Loaded {Count} detection results for training", trainingData.Count);

        // Step 2: Extract features
        var features = _featureExtraction.ExtractBatch(trainingData);

        if (features.Count < 50)
        {
            _logger.LogWarning("Insufficient valid features ({Count} samples). Need at least 50.", features.Count);
            return [];
        }

        // Step 3: Train ML.NET model
        var mlContext = new MLContext(seed: 42);
        var model = TrainVetoPredictionModel(mlContext, features);

        // Step 4: Analyze veto patterns per algorithm
        var vetoStats = AnalyzeVetoPatterns(trainingData);

        // Step 5: Generate recommendations for algorithms with high veto rates
        var config = await _configRepository.GetGlobalConfigAsync(cancellationToken);
        var recommendations = new List<ThresholdRecommendationDto>();

        foreach (var (algorithmName, stats) in vetoStats)
        {
            var vetoRate = stats.TotalFlags > 0
                ? (decimal)stats.VetoedCount / stats.TotalFlags * 100m
                : 0m;

            // Only recommend changes if veto rate > 10% and we have sufficient data
            if (vetoRate > 10m && stats.VetoedCount >= 3)
            {
                var currentThreshold = GetCurrentThreshold(config, algorithmName);
                var recommendedThreshold = FindOptimalThreshold(
                    mlContext,
                    model,
                    features,
                    algorithmName,
                    currentThreshold,
                    vetoRate);

                var recommendation = new ThresholdRecommendationDto
                {
                    AlgorithmName = algorithmName,
                    CurrentThreshold = currentThreshold,
                    RecommendedThreshold = recommendedThreshold,
                    ConfidenceScore = CalculateConfidence(stats.VetoedCount, stats.TotalFlags),
                    VetoRateBefore = vetoRate,
                    EstimatedVetoRateAfter = EstimateVetoRateAfter(vetoRate, recommendedThreshold, currentThreshold),
                    SampleVetoedMessageIds = stats.VetoedMessageIds.Take(10).ToList(),
                    SpamFlagsCount = stats.TotalFlags,
                    VetoedCount = stats.VetoedCount,
                    TrainingPeriodStart = since,
                    TrainingPeriodEnd = DateTimeOffset.UtcNow
                };

                recommendations.Add(recommendation);

                _logger.LogInformation(
                    "Generated ML recommendation for {Algorithm}: {VetoRate:F1}% veto rate, {CurrentThreshold} → {RecommendedThreshold}",
                    algorithmName, vetoRate, currentThreshold, recommendedThreshold);
            }
        }

        _logger.LogInformation("Generated {Count} threshold recommendations using ML.NET", recommendations.Count);
        return recommendations;
    }

    private async Task<List<DetectionResultData>> LoadTrainingDataAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.DetectionResults
            .AsNoTracking()
            .Where(dr => dr.DetectedAt >= since && dr.CheckResultsJson != null)
            .Join(context.Messages,
                dr => dr.MessageId,
                m => m.MessageId,
                (dr, m) => new DetectionResultData
                {
                    DetectionId = dr.Id,
                    CheckResultsJson = dr.CheckResultsJson,
                    IsSpam = dr.IsSpam,
                    MessageLength = m.MessageText != null ? m.MessageText.Length : 0
                })
            .ToListAsync(cancellationToken);
    }

    private class DetectionResultData : FeatureExtractionService.IDetectionResultData
    {
        public long DetectionId { get; set; }
        public string? CheckResultsJson { get; set; }
        public bool IsSpam { get; set; }
        public int MessageLength { get; set; }
    }

    private ITransformer TrainVetoPredictionModel(MLContext mlContext, List<ThresholdOptimizationFeatures> features)
    {
        _logger.LogInformation("Training ML.NET binary classification model with {Count} samples", features.Count);

        // Check if we have both positive and negative examples
        var vetoedCount = features.Count(f => f.WasVetoed);
        var notVetoedCount = features.Count - vetoedCount;

        _logger.LogInformation(
            "Training data distribution: {VetoedCount} vetoed, {NotVetoedCount} not vetoed",
            vetoedCount,
            notVetoedCount);

        if (vetoedCount == 0 || notVetoedCount == 0)
        {
            throw new InvalidOperationException(
                $"Cannot train binary classifier with imbalanced data: {vetoedCount} vetoed, {notVetoedCount} not vetoed. Need both positive and negative examples.");
        }

        // Convert to IDataView
        var dataView = mlContext.Data.LoadFromEnumerable(features);

        // Build training pipeline
        var pipeline = mlContext.Transforms.Concatenate("Features",
                nameof(ThresholdOptimizationFeatures.BayesConfidence),
                nameof(ThresholdOptimizationFeatures.StopWordsConfidence),
                nameof(ThresholdOptimizationFeatures.SimilarityConfidence),
                nameof(ThresholdOptimizationFeatures.CasConfidence),
                nameof(ThresholdOptimizationFeatures.SpacingConfidence),
                nameof(ThresholdOptimizationFeatures.MultiLanguageConfidence),
                nameof(ThresholdOptimizationFeatures.OpenAIConfidence),
                nameof(ThresholdOptimizationFeatures.ThreatIntelConfidence),
                nameof(ThresholdOptimizationFeatures.ImageConfidence),
                nameof(ThresholdOptimizationFeatures.TriggeredCheckCount),
                nameof(ThresholdOptimizationFeatures.AverageConfidence),
                nameof(ThresholdOptimizationFeatures.MaxConfidence),
                nameof(ThresholdOptimizationFeatures.MessageLength),
                nameof(ThresholdOptimizationFeatures.HasUrls),
                nameof(ThresholdOptimizationFeatures.IsMultiLanguage))
            .Append(mlContext.BinaryClassification.Trainers.SdcaLogisticRegression(
                labelColumnName: "Label",
                featureColumnName: "Features"));

        // Train on full dataset (no train/test split to avoid class imbalance in small datasets)
        // This is acceptable for threshold optimization where we're using the model for simulation, not generalization
        var model = pipeline.Fit(dataView);

        _logger.LogInformation(
            "ML.NET model trained on {Count} samples (no test split due to potential class imbalance)",
            features.Count);

        return model;
    }

    private Dictionary<string, AlgorithmVetoStats> AnalyzeVetoPatterns(
        List<DetectionResultData> detections)
    {
        var stats = new Dictionary<string, AlgorithmVetoStats>();

        foreach (var detection in detections)
        {
            try
            {
                if (string.IsNullOrEmpty(detection.CheckResultsJson))
                    continue;

                // Use CheckResultsSerializer to handle both old and new formats
                var checkResults = CheckResultsSerializer.Deserialize(detection.CheckResultsJson);
                if (checkResults == null || !checkResults.Any())
                    continue;

                // Identify if this was vetoed by OpenAI
                var hasOpenAIClean = checkResults.Any(c =>
                    c.CheckName == CheckName.OpenAI && c.Result == CheckResultType.Clean);
                var otherSpamChecks = checkResults.Where(c =>
                    c.CheckName != CheckName.OpenAI && c.Result == CheckResultType.Spam).ToList();
                bool wasVetoed = hasOpenAIClean && otherSpamChecks.Any();

                // Update statistics for each algorithm that flagged spam
                foreach (var check in checkResults.Where(c =>
                    c.Result == CheckResultType.Spam && c.CheckName != CheckName.OpenAI))
                {
                    var checkName = check.CheckName.ToString();
                    if (!stats.ContainsKey(checkName))
                    {
                        stats[checkName] = new AlgorithmVetoStats();
                    }

                    stats[checkName].TotalFlags++;

                    if (wasVetoed)
                    {
                        stats[checkName].VetoedCount++;
                        stats[checkName].VetoedMessageIds.Add(detection.DetectionId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse check_results_json for detection {Id}", detection.DetectionId);
            }
        }

        return stats;
    }

    private decimal FindOptimalThreshold(
        MLContext mlContext,
        ITransformer model,
        List<ThresholdOptimizationFeatures> features,
        string algorithmName,
        decimal? currentThreshold,
        decimal currentVetoRate)
    {
        _logger.LogInformation("Finding optimal threshold for {Algorithm} (current: {Current}, veto rate: {VetoRate:F1}%)",
            algorithmName, currentThreshold, currentVetoRate);

        // Simulate different thresholds and predict veto rate at each
        var predictionEngine = mlContext.Model.CreatePredictionEngine<ThresholdOptimizationFeatures, VetoPrediction>(model);

        var bestThreshold = currentThreshold ?? 75m;
        var lowestVetoRate = currentVetoRate;

        // Test thresholds from current+5 to 95 in increments of 5
        var startThreshold = (int)(currentThreshold ?? 70m) + 5;
        for (int threshold = startThreshold; threshold <= 95; threshold += 5)
        {
            // Simulate: adjust algorithm confidence in features
            var simulatedVetos = 0;
            var relevantSamples = 0;

            foreach (var feature in features)
            {
                var algorithmConfidence = GetAlgorithmConfidence(feature, algorithmName);

                // Only simulate on samples where this algorithm triggered
                if (algorithmConfidence >= threshold)
                {
                    relevantSamples++;

                    // Predict if this would be vetoed
                    var prediction = predictionEngine.Predict(feature);

                    if (prediction.Probability > 0.5)  // Model predicts veto
                    {
                        simulatedVetos++;
                    }
                }
            }

            if (relevantSamples == 0)
                continue;

            var simulatedVetoRate = (decimal)simulatedVetos / relevantSamples * 100m;

            _logger.LogDebug("Threshold {Threshold}: {VetoRate:F1}% predicted veto rate ({Vetoes}/{Total} samples)",
                threshold, simulatedVetoRate, simulatedVetos, relevantSamples);

            // Accept threshold if veto rate drops below 10%
            if (simulatedVetoRate < lowestVetoRate && simulatedVetoRate < 10m)
            {
                bestThreshold = threshold;
                lowestVetoRate = simulatedVetoRate;
            }
        }

        return bestThreshold;
    }

    private static float GetAlgorithmConfidence(ThresholdOptimizationFeatures features, string algorithmName)
    {
        return algorithmName switch
        {
            "Bayes" => features.BayesConfidence,
            "StopWords" => features.StopWordsConfidence,
            "TF-IDF Similarity" => features.SimilarityConfidence,
            "CAS" => features.CasConfidence,
            "Spacing" => features.SpacingConfidence,
            "MultiLanguage" => features.MultiLanguageConfidence,
            "ThreatIntel" => features.ThreatIntelConfidence,
            "Image" => features.ImageConfidence,
            _ => 0f
        };
    }

    private static decimal? GetCurrentThreshold(SpamDetectionConfig config, string algorithmName)
    {
        // Extract threshold from nested config objects
        return algorithmName switch
        {
            "Bayes" => config.Bayes.ConfidenceThreshold,
            "TF-IDF Similarity" => config.Similarity.ConfidenceThreshold,
            "Spacing" => config.Spacing.ConfidenceThreshold,
            _ => null
        };
    }

    private static decimal CalculateConfidence(int vetoedCount, int totalFlags)
    {
        // Higher confidence with more samples
        // Scale from 70% (3 vetoes) to 95% (50+ vetoes)

        if (vetoedCount < 3)
            return 50m;

        if (vetoedCount >= 50)
            return 95m;

        // Linear scale between 70 and 95
        return 70m + ((vetoedCount - 3m) / 47m * 25m);
    }

    private static decimal? EstimateVetoRateAfter(decimal currentVetoRate, decimal recommendedThreshold, decimal? currentThreshold)
    {
        if (currentThreshold == null)
            return null;

        // Simple estimation: reducing veto rate proportionally to threshold increase
        var thresholdIncrease = recommendedThreshold - currentThreshold.Value;
        var estimatedReduction = thresholdIncrease / 10m * currentVetoRate * 0.3m;  // 30% reduction per 10 points

        var estimated = Math.Max(0, currentVetoRate - estimatedReduction);
        return Math.Round(estimated, 1);
    }

    private class AlgorithmVetoStats
    {
        public int TotalFlags { get; set; }
        public int VetoedCount { get; set; }
        public List<long> VetoedMessageIds { get; set; } = [];
    }
}
