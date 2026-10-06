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
/// A user is skipped only when profile scanning is disabled in every chat they have posted in
/// (chats come from message history; a user who never posted follows the global setting).
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

            var examined = 0;
            var attempted = 0;
            var scanned = 0;
            var skipped = 0;
            var scanningDisabled = 0;
            foreach (var user in users.Take(candidateCap))
            {
                // Batch slots go to scan attempts only
                if (attempted >= batchSize)
                    break;

                examined++;
                try
                {
                    // Scan for the user's most recently active chat with profile scanning enabled (it
                    // targets alerts and supplies thresholds). Skip only when every chat they have posted in has
                    // scanning disabled; with no known chat, the global config (chat 0) decides.
                    var (eligible, chat) = await FindScanChatAsync(user.Id, cancellationToken);
                    if (!eligible)
                    {
                        logger.LogDebug("Profile rescan: scanning disabled in every chat of user {UserId}, skipping",
                            user.Id);
                        scanningDisabled++;
                        continue;
                    }

                    attempted++;

                    var result = await profileScanService.ScanUserProfileAsync(
                        user,
                        triggeringChat: chat,
                        cancellationToken);

                    // A name-only scan counts as scanned; a skip reason means nothing was written.
                    if (result.SkipReason is null)
                        scanned++;
                    else
                        skipped++;

                    // Throttle to avoid Telegram FLOOD_WAIT rate limits
                    await Task.Delay(1000, cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Profile rescan: failed to scan user {UserId}, continuing batch", user.Id);
                }
            }

            if (scanningDisabled > 0)
                logger.LogInformation(
                    "Profile rescan: skipped {Count} users, scanning disabled in every chat", scanningDisabled);

            if (attempted == 0)
                logger.LogInformation("Profile rescan: examined {Examined} candidates but attempted no scans", examined);

            logger.LogInformation("Profile rescan: completed {Scanned}/{Attempted} users ({Skipped} skipped)",
                scanned, attempted, skipped);
            success = true;
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

    /// <summary>
    /// The chat to scan the user for: the most recently active of their chats whose effective config has
    /// profile scanning enabled. Not eligible when none has. A user with no known chat is eligible (chat
    /// null) when the global config has profile scanning enabled.
    /// </summary>
    private async Task<(bool Eligible, ChatIdentity? Chat)> FindScanChatAsync(long userId, CancellationToken cancellationToken)
    {
        var chats = await userRepository.GetChatsForUserAsync(userId, cancellationToken);
        if (chats.Count == 0)
            return (await IsProfileScanEnabledAsync(0, cancellationToken), null);

        foreach (var chat in chats)
        {
            if (await IsProfileScanEnabledAsync(chat.Id, cancellationToken))
                return (true, chat);
        }

        return (false, null);
    }

    private async Task<bool> IsProfileScanEnabledAsync(long chatId, CancellationToken cancellationToken)
    {
        var welcomeConfig = await configService.GetEffectiveWelcomeAsync(chatId, cancellationToken);
        return welcomeConfig?.JoinSecurity?.ProfileScan is { Enabled: true };
    }
}
