using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.ContentDetection.Models;

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
