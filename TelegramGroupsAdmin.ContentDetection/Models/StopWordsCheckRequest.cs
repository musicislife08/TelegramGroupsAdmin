namespace TelegramGroupsAdmin.ContentDetection.Models;

/// <summary>
/// Request for StopWords spam check
/// </summary>
public sealed class StopWordsCheckRequest : ContentCheckRequestBase
{
    public required int ConfidenceThreshold { get; init; }
}
