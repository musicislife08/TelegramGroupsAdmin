namespace TelegramGroupsAdmin.ContentDetection.Models;

/// <summary>
/// Request for Similarity (TF-IDF) spam check
/// </summary>
public sealed class SimilarityCheckRequest : ContentCheckRequestBase
{
    public required int MinMessageLength { get; init; }
    public required double SimilarityThreshold { get; init; }
    public required int ConfidenceThreshold { get; init; }
}
