namespace TelegramGroupsAdmin.ContentDetection.Models;

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
