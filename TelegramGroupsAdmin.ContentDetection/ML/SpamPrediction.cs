using Microsoft.ML.Data;

namespace TelegramGroupsAdmin.ContentDetection.ML;

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
