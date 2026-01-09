namespace TelegramGroupsAdmin.ContentDetection.Models;

/// <summary>
/// Request for ML.NET SDCA spam classifier check
/// </summary>
public sealed class SimilarityCheckRequest : ContentCheckRequestBase
{
    public required int MinMessageLength { get; init; }
    public required double SimilarityThreshold { get; init; }
}
