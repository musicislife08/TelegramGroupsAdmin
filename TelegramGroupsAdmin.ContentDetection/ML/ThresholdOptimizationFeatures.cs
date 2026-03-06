using Microsoft.ML.Data;

namespace TelegramGroupsAdmin.ContentDetection.ML;

/// <summary>
/// Feature vector for ML.NET threshold optimization training.
/// Predicts whether a detection would be vetoed by OpenAI.
/// </summary>
public class ThresholdOptimizationFeatures
{
    // Individual algorithm scores (0.0-5.0)

    [LoadColumn(0)]
    public float BayesScore { get; set; }

    [LoadColumn(1)]
    public float StopWordsScore { get; set; }

    [LoadColumn(2)]
    public float SimilarityScore { get; set; }

    [LoadColumn(3)]
    public float CasScore { get; set; }

    [LoadColumn(4)]
    public float SpacingScore { get; set; }

    [LoadColumn(5)]
    public float MultiLanguageScore { get; set; }

    [LoadColumn(6)]
    public float OpenAIScore { get; set; }

    [LoadColumn(7)]
    public float ThreatIntelScore { get; set; }

    [LoadColumn(8)]
    public float ImageScore { get; set; }

    // Aggregate features

    [LoadColumn(9)]
    public float TriggeredCheckCount { get; set; }  // Number of checks that flagged spam

    [LoadColumn(10)]
    public float AverageScore { get; set; }  // Average of all non-zero scores

    [LoadColumn(11)]
    public float MaxScore { get; set; }  // Highest score

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
