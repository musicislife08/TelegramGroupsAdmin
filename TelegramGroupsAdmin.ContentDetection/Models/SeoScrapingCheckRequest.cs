namespace TelegramGroupsAdmin.ContentDetection.Models;

/// <summary>
/// Request for SeoScraping spam check
/// </summary>
public sealed class SeoScrapingCheckRequest : ContentCheckRequestBase
{
    public required int ConfidenceThreshold { get; init; }
}
