using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Configuration.Models.Welcome;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Metrics;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.Telegram.Services.UserApi;

/// <inheritdoc />
public sealed class ProfileScanGate(
    IConfigService configService,
    ITelegramUserRepository userRepository,
    IUsernameHistoryRepository usernameHistory,
    IChatAdminsRepository chatAdminsRepository,
    IProfileScanService profileScanService,
    PipelineMetrics pipelineMetrics,
    ILogger<ProfileScanGate> logger) : IProfileScanGate
{
    public async Task<ProfileScanResult?> ScanIfEligibleAsync(
        UserIdentity user,
        ChatIdentity? chat,
        ProfileScanTrigger trigger,
        CancellationToken ct,
        bool forceRescan = false)
    {
        var config = await GetProfileScanConfigAsync(chat?.Id ?? 0, ct);

        if (config is null || !config.Enabled)
            return Skip("disabled", user, trigger);

        var triggerEnabled = trigger switch
        {
            ProfileScanTrigger.Join => config.ScanOnJoin,
            ProfileScanTrigger.FirstMessage => config.ScanOnFirstMessage,
            ProfileScanTrigger.ProfileChange => config.ScanOnProfileChange,
            ProfileScanTrigger.Rescan => true,
            _ => false
        };

        // A null row means the user is not yet tracked: not trusted, never scanned, so eligible.
        // Read lazily: the trigger_disabled skip below fires on nearly every group message and must
        // stay free of queries.
        Models.TelegramUser? existingUser = null;
        var userLoaded = false;

        // The rename trigger is itself a rename. A join or first message counts as one when a rename
        // was recorded after the last scan (joins record renames without scanning).
        var renamed = trigger == ProfileScanTrigger.ProfileChange;

        // A join records a rename without scanning (nothing slow may run before
        // the joiner is muted). With ScanOnJoin off, a chat that scans on profile
        // changes still scans a joiner who renamed since their last scan.
        if (!triggerEnabled && trigger == ProfileScanTrigger.Join && config.ScanOnProfileChange)
        {
            existingUser = await userRepository.GetByTelegramIdAsync(user.Id, cancellationToken: ct);
            userLoaded = true;
            renamed = existingUser is not null
                && await usernameHistory.HasChangeSinceAsync(user.Id, existingUser.ProfileScannedAt, ct);
            triggerEnabled = renamed;
        }

        if (!triggerEnabled)
        {
            // Not recorded via Skip(): in the shipping configuration
            // (ScanOnFirstMessage off) this fires on nearly every group
            // message, which would drown the genuinely interesting skip
            // reasons on dashboards.
            logger.LogDebug(
                "Profile scan gate skipped {User} for trigger {Trigger}: trigger_disabled",
                user.ToLogDebug(), trigger);
            return null;
        }

        if (!userLoaded)
            existingUser = await userRepository.GetByTelegramIdAsync(user.Id, cancellationToken: ct);

        if (existingUser?.IsTrusted == true)
            return Skip("trusted", user, trigger);

        // Chat-admin trust is only reconciled by ChatHealthCheck (~every 30
        // minutes), so a newly promoted admin can be untrusted for a window
        // after promotion. Without this check, such an admin would fall
        // through to a scan that can globally ban them. Any chat counts: the
        // ban is global, and the rescan job may scan with no chat at all.
        if ((await chatAdminsRepository.GetAdminChatsAsync(user.Id, ct)).Count > 0)
            return Skip("admin", user, trigger);

        // Bots belong to bot protection (joins divert them before the scan).
        // Without this check, a first message or a rescan could scan and
        // globally ban a legitimate third-party bot.
        if (existingUser?.IsBot == true)
            return Skip("bot", user, trigger);

        // A banned user has nothing left to act on.
        if (existingUser?.IsBanned == true)
            return Skip("banned", user, trigger);

        // Join / first message scan a new or never-scanned user, or one who renamed since the last
        // scan. A scanned user who has not renamed is not scanned again (the rescan job, which skips
        // this rule, retries incomplete scans).
        if (trigger is ProfileScanTrigger.Join or ProfileScanTrigger.FirstMessage
            && existingUser?.ProfileScannedAt is { } lastScan)
        {
            renamed = renamed || await usernameHistory.HasChangeSinceAsync(user.Id, lastScan, ct);
            if (!renamed)
                return Skip("already_scanned", user, trigger);
        }

        // The exclude flag is the admin's "don't scan automatically unless something changes":
        // a rename is a change.
        if (existingUser?.ProfileScanExcluded == true && !renamed)
            return Skip("excluded", user, trigger);

        logger.LogDebug(
            "Profile scan gate admitted {User} for trigger {Trigger}",
            user.ToLogDebug(), trigger);

        // No session check: without a usable User API session the service scores the name alone.
        // A rename rescans a known user; joins and first messages are welcome scans (scan-source metric).
        // The rescan job's scans are rescans too.
        var origin = trigger is ProfileScanTrigger.ProfileChange or ProfileScanTrigger.Rescan
            ? ProfileScanOrigin.Rescan
            : ProfileScanOrigin.ChatEvent;
        return await profileScanService.ScanUserProfileAsync(user, chat, ct, forceRescan, origin);
    }

    /// <inheritdoc />
    public async Task<bool> IsScanningEnabledAsync(long chatId, CancellationToken ct) =>
        (await GetProfileScanConfigAsync(chatId, ct))?.Enabled == true;

    private async Task<ProfileScanConfig?> GetProfileScanConfigAsync(long chatId, CancellationToken ct) =>
        (await configService.GetEffectiveWelcomeAsync(chatId, ct: ct))?.JoinSecurity?.ProfileScan;

    private ProfileScanResult? Skip(string reason, UserIdentity user, ProfileScanTrigger trigger)
    {
        pipelineMetrics.RecordProfileScanSkipped(reason);

        logger.LogDebug(
            "Profile scan gate skipped {User} for trigger {Trigger}: {Reason}",
            user.ToLogDebug(), trigger, reason);

        return null;
    }
}
