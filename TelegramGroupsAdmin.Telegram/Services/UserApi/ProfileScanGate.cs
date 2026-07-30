using Microsoft.Extensions.Logging;
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
    IChatAdminsRepository chatAdminsRepository,
    ITelegramSessionManager sessionManager,
    IProfileScanService profileScanService,
    PipelineMetrics pipelineMetrics,
    ILogger<ProfileScanGate> logger) : IProfileScanGate
{
    public async Task<ProfileScanResult?> ScanIfEligibleAsync(
        UserIdentity user,
        ChatIdentity? chat,
        ProfileScanTrigger trigger,
        CancellationToken ct)
    {
        var welcomeConfig = await configService.GetEffectiveWelcomeAsync(chat?.Id ?? 0, ct: ct);
        var config = welcomeConfig?.JoinSecurity?.ProfileScan;

        if (config is null || !config.Enabled)
            return Skip("disabled", user, trigger);

        var triggerEnabled = trigger switch
        {
            ProfileScanTrigger.Join => config.ScanOnJoin,
            ProfileScanTrigger.FirstMessage => config.ScanOnFirstMessage,
            ProfileScanTrigger.ProfileChange => config.ScanOnProfileChange,
            _ => false
        };

        if (!triggerEnabled)
            return Skip("trigger_disabled", user, trigger);

        // A null row means the user is not yet tracked: not trusted, never
        // scanned, so eligible. This is the common case for FirstMessage.
        var existingUser = await userRepository.GetByTelegramIdAsync(user.Id, cancellationToken: ct);

        if (existingUser?.IsTrusted == true)
            return Skip("trusted", user, trigger);

        // Chat-admin trust is only reconciled by ChatHealthCheck (~every 30
        // minutes), so a newly promoted admin can be untrusted for a window
        // after promotion. Without this check, such an admin posting inside
        // that window would fall through to a scan that can globally ban them.
        if (chat is not null && await chatAdminsRepository.IsAdminAsync(chat.Id, user.Id, cancellationToken: ct))
            return Skip("admin", user, trigger);

        if (existingUser?.ProfileScanExcluded == true)
            return Skip("excluded", user, trigger);

        if (trigger == ProfileScanTrigger.FirstMessage && existingUser?.ProfileScannedAt is not null)
            return Skip("already_scanned", user, trigger);

        if (!await sessionManager.HasAnyActiveSessionAsync(ct: ct))
            return Skip("no_session", user, trigger);

        logger.LogDebug(
            "Profile scan gate admitted {User} for trigger {Trigger}",
            user.ToLogDebug(), trigger);

        return await profileScanService.ScanUserProfileAsync(user, chat, ct: ct);
    }

    private ProfileScanResult? Skip(string reason, UserIdentity user, ProfileScanTrigger trigger)
    {
        pipelineMetrics.RecordProfileScanSkipped(reason);

        logger.LogDebug(
            "Profile scan gate skipped {User} for trigger {Trigger}: {Reason}",
            user.ToLogDebug(), trigger, reason);

        return null;
    }
}
