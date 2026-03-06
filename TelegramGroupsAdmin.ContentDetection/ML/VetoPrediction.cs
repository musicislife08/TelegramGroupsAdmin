using Microsoft.ML.Data;

namespace TelegramGroupsAdmin.ContentDetection.ML;

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
