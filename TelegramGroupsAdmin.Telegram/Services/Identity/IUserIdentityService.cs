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
    /// Records the names Telegram reported (newest observation wins) and returns the resulting
    /// identity. When this records a rename, the service decides from the observation's source and
    /// the user whether to rescan the profile inline (see <see cref="UserIdentityService"/>).
    /// </summary>
    Task<UserIdentity> ObserveAsync(ObservedUser observed, ProfileChangeContext context, CancellationToken ct = default);

    /// <summary>Resolves one identity; an unknown id gives an id-only, unscanned identity.</summary>
    Task<UserIdentity> ResolveAsync(long userId, CancellationToken ct = default);

    /// <summary>Resolves many identities in one query, in the requested order.</summary>
    Task<IReadOnlyList<UserIdentity>> ResolveManyAsync(IReadOnlyCollection<long> userIds, CancellationToken ct = default);
}
