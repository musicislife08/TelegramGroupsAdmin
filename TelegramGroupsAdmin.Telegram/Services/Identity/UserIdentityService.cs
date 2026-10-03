using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Core;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.UserApi;

namespace TelegramGroupsAdmin.Telegram.Services.Identity;

/// <summary>
/// The single way to obtain a UserIdentity for bot-facing work. Records observed names (newest
/// observation wins) and reads identities from the user_identities view. Stateless.
/// </summary>
public sealed class UserIdentityService(
    ITelegramUserRepository users,
    IProfileScanGate scanGate,
    ILogger<UserIdentityService> logger) : IUserIdentityService
{
    public async Task<UserIdentity> ObserveAsync(
        ObservedUser observed, ProfileChangeContext context, RenameRescan rescan, CancellationToken ct = default)
    {
        ObservedNamesResult result;
        try
        {
            result = await users.GetOrUpdateAsync(observed, context, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Recording what we saw must never cost the update its moderation.
            logger.LogError(ex, "Failed to record observed names for user {UserId}", observed.Id);
            return new UserIdentity(observed.Id, observed.FirstName, observed.LastName, observed.Username);
        }

        UserIdentity identity;
        try
        {
            identity = (await ReadManyAsync([observed.Id], ct))[0];
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // A failed read must not cost the update its moderation either; the observed
            // names are better than the id-only fallback ResolveAsync would give.
            logger.LogError(ex, "Failed to resolve identity for user {UserId} after recording names", observed.Id);
            return new UserIdentity(observed.Id, observed.FirstName, observed.LastName, observed.Username);
        }

        // Inline, as the message pipeline did before: a profile ban stays inside this update's
        // context and its existing cleanup path. Renames are rare, so the stall is rare.
        if (rescan == RenameRescan.Inline && result.Renamed is not null
            && !result.User.IsTrusted && !result.User.IsBot && !TelegramConstants.IsSystemUser(observed.Id))
        {
            try
            {
                await scanGate.ScanIfEligibleAsync(identity, context.Chat, ProfileScanTrigger.ProfileChange, ct, forceRescan: true);
                identity = await ResolveAsync(observed.Id, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Profile rescan after rename failed for user {UserId}", observed.Id);
            }
        }

        return identity;
    }

    public async Task<UserIdentity> ResolveAsync(long userId, CancellationToken ct = default) =>
        (await ResolveManyAsync([userId], ct))[0];

    public async Task<IReadOnlyList<UserIdentity>> ResolveManyAsync(IReadOnlyCollection<long> userIds, CancellationToken ct = default)
    {
        try
        {
            return await ReadManyAsync(userIds, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // An identity lookup must never block moderation or /start: fall back to id-only
            // (unscanned) identities, as ObserveAsync falls back to the observed names.
            logger.LogWarning(ex, "Failed to resolve identities for {Count} user(s); using id-only identities", userIds.Count);
            return userIds.Select(UserIdentity.FromId).ToList();
        }
    }

    private async Task<IReadOnlyList<UserIdentity>> ReadManyAsync(IReadOnlyCollection<long> userIds, CancellationToken ct)
    {
        var found = (await users.GetIdentitiesAsync(userIds, ct)).ToDictionary(i => i.Id);
        return userIds.Select(id => found.TryGetValue(id, out var identity) ? identity : UserIdentity.FromId(id)).ToList();
    }
}
