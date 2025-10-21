using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.ContentDetection.Models;

/// <summary>
/// Domain filter (UI model with actor resolution)
/// Manual domain entries (blacklist/whitelist)
/// </summary>
public record DomainFilter(
    long Id,
    long ChatId,  // 0 = global, non-zero = chat-specific
    string Domain,
    DomainFilterType FilterType,
    BlockMode BlockMode,  // Ignored for whitelist
    bool Enabled,
    Actor AddedBy,
    DateTimeOffset AddedDate,
    string? Notes
);
