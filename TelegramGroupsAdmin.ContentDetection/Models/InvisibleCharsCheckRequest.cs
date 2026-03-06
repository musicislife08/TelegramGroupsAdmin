namespace TelegramGroupsAdmin.ContentDetection.Models;

/// <summary>
/// Request for InvisibleChars spam check
/// </summary>
public sealed class InvisibleCharsCheckRequest : ContentCheckRequestBase
{
    public required double ConfidenceThreshold { get; init; }
}
