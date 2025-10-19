using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.ContentDetection.Models;

// ============================================================================
// Enums - Phase 4.13: URL Filtering
// ============================================================================

/// <summary>
/// Blocklist file format specification for parsing domain lists
/// </summary>
public enum BlocklistFormat
{
    /// <summary>Block List Project format - one domain per line with # for comments</summary>
    NewlineDomains = 0,

    /// <summary>Hosts file format - 0.0.0.0 or 127.0.0.1 prefix before domain</summary>
    HostsFile = 1,

    /// <summary>CSV format - domain in first column, header row skipped</summary>
    Csv = 2
}

/// <summary>
/// URL filtering enforcement mode
/// </summary>
public enum BlockMode
{
    /// <summary>URL filtering disabled for this source</summary>
    Disabled = 0,

    /// <summary>Soft block - contributes to spam confidence voting, subject to OpenAI veto</summary>
    Soft = 1,

    /// <summary>Hard block - instant ban before spam detection, no OpenAI veto</summary>
    Hard = 2
}

/// <summary>
/// Domain filter type classification
/// </summary>
public enum DomainFilterType
{
    /// <summary>Blacklist - domain is blocked</summary>
    Blacklist = 0,

    /// <summary>Whitelist - domain bypasses all checks</summary>
    Whitelist = 1
}

// ============================================================================
// UI Models - Phase 4.13: URL Filtering
// ============================================================================

/// <summary>
/// Blocklist subscription (UI model with actor resolution)
/// External URL-based blocklists (Block List Project + custom)
/// </summary>
public record BlocklistSubscription(
    long Id,
    long? ChatId,
    string Name,
    string Url,
    BlocklistFormat Format,
    BlockMode BlockMode,
    bool IsBuiltIn,
    bool Enabled,
    DateTimeOffset? LastFetched,
    int? EntryCount,
    int RefreshIntervalHours,
    Actor AddedBy,
    DateTimeOffset AddedDate,
    string? Notes
);

/// <summary>
/// Domain filter (UI model with actor resolution)
/// Manual domain entries (blacklist/whitelist)
/// </summary>
public record DomainFilter(
    long Id,
    long? ChatId,
    string Domain,
    DomainFilterType FilterType,
    BlockMode BlockMode,  // Ignored for whitelist
    bool Enabled,
    Actor AddedBy,
    DateTimeOffset AddedDate,
    string? Notes
);

/// <summary>
/// Cached blocked domain (UI model)
/// Normalized, deduplicated domains from all enabled blocklists
/// </summary>
public record CachedBlockedDomain(
    long Id,
    string Domain,
    BlockMode BlockMode,
    long? ChatId,
    long? SourceSubscriptionId,
    DateTimeOffset FirstSeen,
    DateTimeOffset LastVerified,
    string? Notes
);

/// <summary>
/// Result from hard block pre-filter check
/// </summary>
public record HardBlockResult(
    bool ShouldBlock,
    string? Reason,
    string? BlockedDomain
);

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
