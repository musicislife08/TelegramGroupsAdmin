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
