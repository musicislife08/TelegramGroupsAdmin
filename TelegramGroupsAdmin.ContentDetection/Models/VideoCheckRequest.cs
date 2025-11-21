namespace TelegramGroupsAdmin.ContentDetection.Models;

/// <summary>
/// Request for Video spam check (Frame extraction + keyframe hash + OCR + OpenAI Vision, ML-6)
/// </summary>
public sealed class VideoCheckRequest : ContentCheckRequestBase
{
    public required string VideoLocalPath { get; init; }
    public required string? CustomPrompt { get; init; }
    public required int ConfidenceThreshold { get; init; }
}
