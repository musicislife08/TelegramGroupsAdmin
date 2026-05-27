using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Checks user display names against the username blacklist.
/// Designed behind an interface so the same matching logic can be
/// reused in the content detection pipeline later.
/// </summary>
public interface IUsernameBlacklistService
{
    /// <summary>
    /// Check if a display name matches any enabled blacklist entry.
    /// Returns the matched entry or null if no match.
    /// </summary>
    Task<UsernameBlacklistEntry?> CheckDisplayNameAsync(
        string displayName,
        CancellationToken cancellationToken = default);

    Task<long> AddEntryAsync(
        string pattern,
        BlacklistMatchType matchType,
        string? notes,
        Actor actor,
        CancellationToken ct = default);

    Task<bool> DeleteEntryAsync(long id, string pattern, Actor actor, CancellationToken ct = default);

    Task<bool> SetEnabledAsync(long id, string pattern, bool enabled, Actor actor, CancellationToken ct = default);

    Task<bool> UpdateNotesAsync(long id, string pattern, string? notes, Actor actor, CancellationToken ct = default);
}
