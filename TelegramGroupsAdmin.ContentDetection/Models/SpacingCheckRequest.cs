namespace TelegramGroupsAdmin.ContentDetection.Models;

/// <summary>
/// Request for Spacing/InvisibleChars spam check
/// </summary>
public sealed class SpacingCheckRequest : ContentCheckRequestBase
{
    public required int ConfidenceThreshold { get; init; }
    public required double SuspiciousRatioThreshold { get; init; }
}
