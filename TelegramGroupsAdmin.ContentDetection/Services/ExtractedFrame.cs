namespace TelegramGroupsAdmin.ContentDetection.Services;

/// <summary>
/// Metadata for an extracted video frame
/// </summary>
public record ExtractedFrame(
    string FramePath,
    double PositionPercent,
    double Brightness,
    bool IsBlackFrame
);
