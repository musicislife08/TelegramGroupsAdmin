namespace TelegramGroupsAdmin.Data.Models.Configs;

/// <summary>
/// Data layer representation of VideoContentConfig for EF Core JSON column mapping.
/// </summary>
public class VideoContentConfigData
{
    public bool UseGlobal { get; set; } = true;

    public bool Enabled { get; set; } = true;

    public bool UseOpenAIVision { get; set; } = true;

    public bool UseOCR { get; set; } = true;

    public int OcrConfidenceThreshold { get; set; } = 75;

    public int MinOcrTextLength { get; set; } = 10;

    public bool UseHashSimilarity { get; set; } = true;

    public double HashSimilarityThreshold { get; set; } = 0.85;

    public int HashMatchConfidence { get; set; } = 95;

    public int MaxTrainingSamplesToCompare { get; set; } = 1000;

    /// <summary>
    /// Timeout in seconds. Stored as double to avoid Npgsql interval parsing issues with ToJson().
    /// </summary>
    public double TimeoutSeconds { get; set; } = 60;

    public bool AlwaysRun { get; set; }
}
