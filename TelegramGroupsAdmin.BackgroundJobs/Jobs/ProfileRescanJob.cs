using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Quartz;
using TelegramGroupsAdmin.AI.Services;
using TelegramGroupsAdmin.BackgroundJobs.Metrics;
using TelegramGroupsAdmin.BackgroundJobs.Services;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Models.BackgroundJobSettings;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Identity;
using TelegramGroupsAdmin.Telegram.Services.UserApi;

namespace TelegramGroupsAdmin.BackgroundJobs.Jobs;

/// <summary>
/// Periodic job that retries incomplete profile scans (never scanned, or a name-only latest scan under
/// the retry limit). Every scan falls back to name-only, so the job runs with or without a User API session,
/// but not when there is neither a session nor the profile-scan AI (no scan could write anything).
/// Chats come from message history, active managed chats only. A user is skipped when profile scanning
/// is disabled in every active managed chat they have posted in, and when they have posted only in
/// chats the bot no longer manages (no longer a user: no action at all). A user who never posted
/// follows the global setting and is scanned with no chat, so a ban posts no celebration.
/// </summary>
[DisallowConcurrentExecution]
public class ProfileRescanJob(
    ILogger<ProfileRescanJob> logger,
    IBackgroundJobConfigService jobConfigService,
    IConfigService configService,
    ITelegramUserRepository userRepository,
    IProfileScanService profileScanService,
    IUserIdentityService identityService,
    ITelegramSessionManager sessionManager,
    IChatService chatService,
    JobMetrics jobMetrics) : IJob
{
    /// <summary>
    /// Candidates examined per run, as a multiple of BatchSize: users skipped because scanning is disabled in
    /// every chat they have posted in don't use up batch slots, but a large backlog of them can't make a run unbounded.
    /// </summary>
    internal const int CandidatesPerBatchSlot = 10;

    /// <summary>Pause after every scan attempt, to stay clear of Telegram FLOOD_WAIT limits.</summary>
    internal TimeSpan ScanThrottle { get; init; } = TimeSpan.FromSeconds(1);

    public async Task Execute(IJobExecutionContext context)
    {
        await ExecuteAsync(context.CancellationToken);
    }

    private async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        const string jobName = "ProfileRescan";
        var startTimestamp = Stopwatch.GetTimestamp();
        var success = false;

        try
        {
            // With no User API session every scan falls back to name-only, and that needs the
            // profile-scan AI: without either, every attempt fails without writing anything, so the
            // same never-scanned users would take the batch on every run. End the run instead.
            if (!await sessionManager.HasAnyActiveSessionAsync(cancellationToken)
                && !await chatService.IsFeatureAvailableAsync(AIFeatureType.ProfileScan, cancellationToken))
            {
                logger.LogWarning("Profile rescan skipped: no User API session and the profile-scan AI is unavailable");
                success = true;
                return;
            }

            // Load job-specific settings
            var jobConfig = await jobConfigService.GetJobConfigAsync(
                BackgroundJobNames.ProfileRescan, cancellationToken);
            var settings = jobConfig?.ProfileRescan ?? new();
            var batchSize = settings.BatchSize;
            var rescanAfter = TimeSpanUtilities.ParseDurationOrDefault(settings.RescanAfter, TimeSpan.FromDays(7));
            var cutoff = DateTimeOffset.UtcNow - rescanAfter;

            logger.LogInformation(
                "Profile rescan: starting batch (size={BatchSize}, rescanAfter={RescanAfter}, cutoff={Cutoff}, nameOnlyRetryLimit={RetryLimit})",
                batchSize, settings.RescanAfter, cutoff, settings.NameOnlyRetryLimit);

            // One ordered candidate list, capped, rather than offset pages: a scan changes the user's
            // profile_scanned_at and scan rows, so offset pages would shift under the loop.
            var candidateCap = batchSize * CandidatesPerBatchSlot;
            var userIds = await userRepository.GetEligibleUsersForRescanAsync(
                candidateCap, cutoff, settings.NameOnlyRetryLimit, cancellationToken);

            if (userIds.Count == 0)
            {
                logger.LogInformation("Profile rescan: no eligible users found");
                success = true;
                return;
            }

            logger.LogInformation("Profile rescan: found {Count} candidates", userIds.Count);

            // One lookup for the candidates; ResolveManyAsync keeps the requested order
            var users = await identityService.ResolveManyAsync(userIds, cancellationToken);

            // Effective config per chat, read once per run: candidates share chats.
            var scanEnabledByChat = new Dictionary<long, bool>();

            var examined = 0;
            var attempted = 0;
            var scanned = 0;
            var skipped = 0;
            var scanningDisabled = 0;
            var noLongerUsers = 0;
            foreach (var user in users)
            {
                // Batch slots go to scan attempts only
                if (attempted >= batchSize)
                    break;

                examined++;
                // Every candidate not skipped (scanning disabled, or no longer a user) is an attempt: it takes
                // a batch slot and is throttled, whether it scans, skips or fails (a failed chat lookup included).
                var isAttempt = true;
                try
                {
                    // Scan for the user's most recently active managed chat with profile scanning enabled
                    // (it targets alerts and supplies thresholds). A user who never posted follows the
                    // global config (chat 0).
                    var (skip, chat) = await FindScanChatAsync(user.Id, scanEnabledByChat, cancellationToken);
                    if (skip == ScanChatSkip.NoLongerAUser)
                    {
                        isAttempt = false;
                        logger.LogDebug("Profile rescan: user {UserId} only posted in chats no longer managed, skipping",
                            user.Id);
                        noLongerUsers++;
                        continue;
                    }

                    if (skip == ScanChatSkip.ScanningDisabled)
                    {
                        isAttempt = false;
                        logger.LogDebug("Profile rescan: scanning disabled in every chat of user {UserId}, skipping",
                            user.Id);
                        scanningDisabled++;
                        continue;
                    }

                    var result = await profileScanService.ScanUserProfileAsync(
                        user,
                        triggeringChat: chat,
                        cancellationToken,
                        origin: ProfileScanOrigin.Rescan);

                    // A name-only scan counts as scanned; a skip reason means nothing was written.
                    if (result.SkipReason is null)
                        scanned++;
                    else
                        skipped++;
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    logger.LogWarning(ex, "Profile rescan: failed to scan user {UserId}, continuing batch", user.Id);
                }

                if (isAttempt)
                {
                    attempted++;
                    // Throttle to avoid Telegram FLOOD_WAIT rate limits
                    await Task.Delay(ScanThrottle, cancellationToken);
                }
            }

            if (scanningDisabled > 0)
                logger.LogInformation(
                    "Profile rescan: skipped {Count} users, scanning disabled in every chat", scanningDisabled);

            if (noLongerUsers > 0)
                logger.LogInformation(
                    "Profile rescan: skipped {Count} users who only posted in chats no longer managed", noLongerUsers);

            if (attempted == 0)
                logger.LogInformation("Profile rescan: examined {Examined} candidates but attempted no scans", examined);

            logger.LogInformation("Profile rescan: completed {Scanned}/{Attempted} users ({Skipped} skipped)",
                scanned, attempted, skipped);
            success = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown, not a failure
            logger.LogInformation("Profile rescan cancelled");
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Profile rescan batch failed");
            throw; // Re-throw for Quartz retry
        }
        finally
        {
            var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
            jobMetrics.RecordJobExecution(jobName, success, elapsedMs);
        }
    }

    private enum ScanChatSkip
    {
        None,
        /// <summary>Profile scanning is disabled in every active managed chat the user posted in.</summary>
        ScanningDisabled,
        /// <summary>The user posted, but only in chats the bot no longer manages: no longer a user.</summary>
        NoLongerAUser
    }

    /// <summary>
    /// The chat to scan the user for: the most recently active of their active managed chats whose
    /// effective config has profile scanning enabled. A user with no active managed chat is no longer a
    /// user when they have message history elsewhere; one who never posted is scanned with no chat when
    /// the global config has profile scanning enabled.
    /// </summary>
    private async Task<(ScanChatSkip Skip, ChatIdentity? Chat)> FindScanChatAsync(
        long userId, Dictionary<long, bool> scanEnabledByChat, CancellationToken cancellationToken)
    {
        var chats = await userRepository.GetChatsForUserAsync(userId, cancellationToken);
        if (chats.Count == 0)
        {
            if (await userRepository.HasMessageHistoryAsync(userId, cancellationToken))
                return (ScanChatSkip.NoLongerAUser, null);

            return await IsProfileScanEnabledAsync(0, scanEnabledByChat, cancellationToken)
                ? (ScanChatSkip.None, null)
                : (ScanChatSkip.ScanningDisabled, null);
        }

        foreach (var chat in chats)
        {
            if (await IsProfileScanEnabledAsync(chat.Id, scanEnabledByChat, cancellationToken))
                return (ScanChatSkip.None, chat);
        }

        return (ScanChatSkip.ScanningDisabled, null);
    }

    private async Task<bool> IsProfileScanEnabledAsync(
        long chatId, Dictionary<long, bool> scanEnabledByChat, CancellationToken cancellationToken)
    {
        if (scanEnabledByChat.TryGetValue(chatId, out var enabled))
            return enabled;

        var welcomeConfig = await configService.GetEffectiveWelcomeAsync(chatId, cancellationToken);
        enabled = welcomeConfig?.JoinSecurity?.ProfileScan is { Enabled: true };
        scanEnabledByChat[chatId] = enabled;
        return enabled;
    }
}
