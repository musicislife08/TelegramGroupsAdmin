using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Domain model for a username blacklist entry.
/// Repos accept/return this; never expose the Dto.
/// </summary>
public sealed record UsernameBlacklistEntry(
    long Id,
    string Pattern,
    BlacklistMatchType MatchType,
    bool Enabled,
    DateTimeOffset CreatedAt,
    Actor CreatedBy,
    string? Notes);
