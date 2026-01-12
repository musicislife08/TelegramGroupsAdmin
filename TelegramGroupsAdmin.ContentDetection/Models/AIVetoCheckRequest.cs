namespace TelegramGroupsAdmin.ContentDetection.Models;

/// <summary>
/// Request for AI veto spam check - AI always runs as veto to confirm/override other spam checks
/// </summary>
public sealed class AIVetoCheckRequest : ContentCheckRequestBase
{
    public required string? SystemPrompt { get; init; }
    public required bool HasSpamFlags { get; init; }
    public required int MinMessageLength { get; init; }
    public required bool CheckShortMessages { get; init; }
    public required int MessageHistoryCount { get; init; }
    public required string Model { get; init; }
    public required int MaxTokens { get; init; }

    /// <summary>
    /// OCR-extracted text from image (combined with caption for veto analysis)
    /// </summary>
    public string? OcrExtractedText { get; init; }

    /// <summary>
    /// Raw OpenAI Vision analysis (reason/patterns from image spam detection)
    /// </summary>
    public string? VisionAnalysisText { get; init; }
}
