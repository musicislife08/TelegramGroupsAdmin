namespace TelegramGroupsAdmin.ContentDetection.Models;

/// <summary>
/// Request for ThreatIntel spam check (VirusTotal, Safe Browsing)
/// </summary>
public sealed class ThreatIntelCheckRequest : ContentCheckRequestBase
{
    public required List<string> Urls { get; init; }
    public required string? VirusTotalApiKey { get; init; }
    public required int ConfidenceThreshold { get; init; }
}
