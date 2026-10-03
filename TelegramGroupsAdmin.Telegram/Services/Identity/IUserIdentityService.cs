using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Services.Identity;

/// <summary>
/// The single way to obtain a UserIdentity for bot-facing work: records observed names and
/// resolves identities (names plus name verdict) by user id.
/// </summary>
public interface IUserIdentityService
{
    /// <summary>
    /// Records the names Telegram reported (newest observation wins), optionally rescans the
    /// profile inline when this changed them, and returns the resulting identity.
    /// </summary>
    Task<UserIdentity> ObserveAsync(ObservedUser observed, ProfileChangeContext context, RenameRescan rescan, CancellationToken ct = default);

    /// <summary>Resolves one identity; an unknown id gives an id-only, unscanned identity.</summary>
    Task<UserIdentity> ResolveAsync(long userId, CancellationToken ct = default);

    /// <summary>Resolves many identities in one query, in the requested order.</summary>
    Task<IReadOnlyList<UserIdentity>> ResolveManyAsync(IReadOnlyCollection<long> userIds, CancellationToken ct = default);
}
