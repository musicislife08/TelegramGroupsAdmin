namespace TelegramGroupsAdmin.ContentDetection.Models;

/// <summary>
/// Statistics for URL filtering system
/// </summary>
public record UrlFilterStats(
    int TotalSubscriptions,
    int EnabledSubscriptions,
    int HardBlockSubscriptions,
    int SoftBlockSubscriptions,
    int TotalCachedDomains,
    int HardBlockDomains,
    int SoftBlockDomains,
    int WhitelistedDomains,
    DateTimeOffset? LastSync
);
