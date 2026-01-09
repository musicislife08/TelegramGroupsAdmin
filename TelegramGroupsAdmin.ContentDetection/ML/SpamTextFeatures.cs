using Microsoft.ML.Data;

namespace TelegramGroupsAdmin.ContentDetection.ML;

/// <summary>
/// Feature vector for ML.NET SDCA spam classification.
/// TEXT ONLY - uses TF-IDF transformation (same input as current Similarity/Bayes checks).
/// </summary>
public class SpamTextFeatures
{
    /// <summary>
    /// Message text content (or translated text if available).
    /// Transformed to TF-IDF features by ML.NET pipeline.
    /// </summary>
    [LoadColumn(0)]
    public string MessageText { get; set; } = string.Empty;

    /// <summary>
    /// Label: true = spam, false = ham.
    /// </summary>
    [LoadColumn(1)]
    [ColumnName("Label")]
    public bool IsSpam { get; set; }
}

/// <summary>
/// Prediction output from SDCA spam classifier.
/// </summary>
public class SpamPrediction
{
    /// <summary>
    /// Predicted label: true = spam, false = ham.
    /// </summary>
    [ColumnName("PredictedLabel")]
    public bool IsSpam { get; set; }

    /// <summary>
    /// Probability of being spam (0.0 to 1.0).
    /// Used for score mapping in SimilarityContentCheckV2.
    /// </summary>
    [ColumnName("Probability")]
    public float Probability { get; set; }

    /// <summary>
    /// Raw model score (before sigmoid transformation).
    /// </summary>
    [ColumnName("Score")]
    public float Score { get; set; }
}
