namespace TelegramGroupsAdmin.ContentDetection.Models;

/// <summary>
/// Request for UrlBlocklist spam check
/// </summary>
public sealed class UrlBlocklistCheckRequest : ContentCheckRequestBase
{
    public required List<string> Urls { get; init; }
    public required double ConfidenceThreshold { get; init; }
}
