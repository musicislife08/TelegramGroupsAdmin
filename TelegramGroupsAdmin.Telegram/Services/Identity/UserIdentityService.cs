using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Core;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Helpers;
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
    IUserActionsRepository actions,
    ILogger<UserIdentityService> logger) : IUserIdentityService
{
    public async Task<UserIdentity> ObserveAsync(
        ObservedUser observed, ProfileChangeContext context, CancellationToken ct = default)
    {
        ObservedNamesResult result;
        try
        {
            result = await users.GetOrUpdateAsync(observed, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Recording what we saw must never cost the update its moderation.
            logger.LogError(ex, "Failed to record observed names for user {UserId}", observed.Id);
            return FromObservedNames(observed);
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
            if (result.Renamed is not null)
                await AuditRenameAsync(observed, result.Renamed, context.Chat, ct);
            return FromObservedNames(observed);
        }

        // Inline, as the message pipeline did before: a profile ban stays inside this update's
        // context and its existing cleanup path. Renames are rare, so the stall is rare.
        if (result.Renamed is not null && RescansRename(observed, result.User))
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

        // After the rename is stored and the rescan done, so the audit can never undo the rename.
        if (result.Renamed is not null)
            await AuditRenameAsync(observed, result.Renamed, context.Chat, ct);

        return identity;
    }

    /// <summary>
    /// Writes the ProfileChange audit for a recorded rename: the chat only, no message id (the
    /// triggering message is not stored yet, so a message reference would fail the foreign key). A
    /// failed audit costs the audit row, nothing else.
    /// </summary>
    private async Task AuditRenameAsync(ObservedUser observed, PreviousNames previous, ChatIdentity? chat, CancellationToken ct)
    {
        try
        {
            var reason = ProfileChangeReason.Build(previous, observed);
            await actions.InsertAsync(new UserActionRecord(
                Id: 0,
                UserId: observed.Id,
                ActionType: UserActionType.ProfileChange,
                MessageId: null,
                ChatId: chat?.Id,
                IssuedBy: Actor.ProfileDiffDetection,
                IssuedAt: DateTimeOffset.UtcNow,
                ExpiresAt: null,
                Reason: AuditReason.WithChatTag(chat, reason)), ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Failed to audit rename of user {UserId}; names were recorded", observed.Id);
        }
    }

    /// <summary>
    /// The fallback identity when recording or reading fails: the observed names with
    /// <see cref="NameVerdict.Unscanned"/>, so masking fails open by design. A lookup failure must
    /// never block moderation, and an unscanned name is shown as it is.
    /// </summary>
    private static UserIdentity FromObservedNames(ObservedUser observed) =>
        new UserIdentity(observed.Id, observed.FirstName, observed.LastName, observed.Username);

    /// <summary>
    /// Whether a recorded rename is rescanned now. Only a message or edit (BotUpdate) of a user worth
    /// protecting against: joins and admin refresh (ChatMember) record only, because muting is among
    /// the first join steps and the join flow's own scan sees the rename in username_history; the
    /// scan's own observation (UserApiScan) records only, or it would recurse. Trusted users (all
    /// chat admins), banned users, bots and system accounts record only.
    /// </summary>
    private static bool RescansRename(ObservedUser observed, TelegramUser user) =>
        observed.Source == ObservationSource.BotUpdate
        && !user.IsTrusted && !user.IsBanned && !user.IsBot && !observed.IsBot
        && !TelegramConstants.IsSystemUser(observed.Id);

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
