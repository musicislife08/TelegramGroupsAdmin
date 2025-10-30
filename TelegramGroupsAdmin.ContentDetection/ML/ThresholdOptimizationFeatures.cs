using Microsoft.ML.Data;

namespace TelegramGroupsAdmin.ContentDetection.ML;

/// <summary>
/// Feature vector for ML.NET threshold optimization training.
/// Predicts whether a detection would be vetoed by OpenAI.
/// </summary>
public class ThresholdOptimizationFeatures
{
    // Individual algorithm confidence scores (0-100)

    [LoadColumn(0)]
    public float BayesConfidence { get; set; }

    [LoadColumn(1)]
    public float StopWordsConfidence { get; set; }

    [LoadColumn(2)]
    public float SimilarityConfidence { get; set; }

    [LoadColumn(3)]
    public float CasConfidence { get; set; }

    [LoadColumn(4)]
    public float SpacingConfidence { get; set; }

    [LoadColumn(5)]
    public float MultiLanguageConfidence { get; set; }

    [LoadColumn(6)]
    public float OpenAIConfidence { get; set; }

    [LoadColumn(7)]
    public float ThreatIntelConfidence { get; set; }

    [LoadColumn(8)]
    public float ImageConfidence { get; set; }

    // Aggregate features

    [LoadColumn(9)]
    public float TriggeredCheckCount { get; set; }  // Number of checks that flagged spam

    [LoadColumn(10)]
    public float AverageConfidence { get; set; }  // Average of all non-zero confidences

    [LoadColumn(11)]
    public float MaxConfidence { get; set; }  // Highest confidence score

    // Message metadata

    [LoadColumn(12)]
    public float MessageLength { get; set; }  // Character count

    [LoadColumn(13)]
    public float HasUrls { get; set; }  // Binary: 0 or 1

    [LoadColumn(14)]
    public float IsMultiLanguage { get; set; }  // Binary: 0 or 1

    // Label - what we're predicting

    [LoadColumn(15)]
    [ColumnName("Label")]
    public bool WasVetoed { get; set; }  // True if OpenAI vetoed this detection
}

/// <summary>
/// Prediction output from ML.NET model
/// </summary>
public class VetoPrediction
{
    [ColumnName("PredictedLabel")]
    public bool WillBeVetoed { get; set; }

    [ColumnName("Probability")]
    public float Probability { get; set; }  // Probability of being vetoed (0-1)

    [ColumnName("Score")]
    public float Score { get; set; }  // Raw model score
}
