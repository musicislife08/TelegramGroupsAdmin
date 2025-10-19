namespace TelegramGroupsAdmin.ContentDetection.Models;

/// <summary>
/// Request for Image spam check (OpenAI Vision)
/// </summary>
public sealed class ImageCheckRequest : ContentCheckRequestBase
{
    public required string PhotoFileId { get; init; }
    public required string? PhotoUrl { get; init; }
    public required string? CustomPrompt { get; init; }
    public required int ConfidenceThreshold { get; init; }
    public required string ApiKey { get; init; }
}
