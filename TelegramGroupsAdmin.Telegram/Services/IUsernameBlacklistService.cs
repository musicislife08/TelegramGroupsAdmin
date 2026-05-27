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

    /// <summary>
    /// Adds a new blacklist entry and emits a BlacklistEntryAdded audit event.
    /// Audit always fires alongside the write — callers cannot opt out.
    /// </summary>
    Task<long> AddEntryAsync(
        string pattern,
        BlacklistMatchType matchType,
        string? notes,
        Actor actor,
        CancellationToken ct = default);

    /// <summary>
    /// Removes the entry with the given id. Returns true if a row was deleted;
    /// the BlacklistEntryRemoved audit event fires only on actual deletion.
    /// </summary>
    Task<bool> DeleteEntryAsync(long id, string pattern, Actor actor, CancellationToken ct = default);

    /// <summary>
    /// Toggles entry enabled state. Writes either BlacklistEntryEnabled or
    /// BlacklistEntryDisabled depending on the new state; audit fires only on
    /// successful update.
    /// </summary>
    Task<bool> SetEnabledAsync(long id, string pattern, bool enabled, Actor actor, CancellationToken ct = default);

    /// <summary>
    /// Updates the entry's free-text notes. Returns true on actual update;
    /// the BlacklistEntryNotesChanged audit event fires only on successful update.
    /// </summary>
    Task<bool> UpdateNotesAsync(long id, string pattern, string? notes, Actor actor, CancellationToken ct = default);
}
