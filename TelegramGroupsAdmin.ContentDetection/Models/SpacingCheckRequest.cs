namespace TelegramGroupsAdmin.ContentDetection.Models;

/// <summary>
/// Request for Spacing/InvisibleChars spam check
/// </summary>
public sealed class SpacingCheckRequest : ContentCheckRequestBase
{
    public required double SuspiciousRatioThreshold { get; init; }
    public required int ShortWordLength { get; init; }
    public required int MinWordsCount { get; init; }
}
