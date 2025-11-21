namespace TelegramGroupsAdmin.ContentDetection.Models;

/// <summary>
/// Request for Image spam check (OCR + OpenAI Vision, ML-5)
/// </summary>
public sealed class ImageCheckRequest : ContentCheckRequestBase
{
    public required string PhotoFileId { get; init; }
    public required string? PhotoUrl { get; init; }
    public string? PhotoLocalPath { get; init; } // ML-5: Local file path for OCR (optional)
    public required string? CustomPrompt { get; init; }
    public required int ConfidenceThreshold { get; init; }
}
