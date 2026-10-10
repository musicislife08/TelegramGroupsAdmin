using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Quartz;
using TelegramGroupsAdmin.AI.Services;
using TelegramGroupsAdmin.BackgroundJobs.Metrics;
using TelegramGroupsAdmin.BackgroundJobs.Services;
using TelegramGroupsAdmin.Configuration.Models;
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
/// Every scan goes through <see cref="IProfileScanGate"/> with the rescan trigger, so the job uses the
/// same eligibility rules as every other automatic scan.
/// </summary>
[DisallowConcurrentExecution]
public class ProfileRescanJob(
    ILogger<ProfileRescanJob> logger,
    IBackgroundJobConfigService jobConfigService,
    ITelegramUserRepository userRepository,
    IProfileScanGate scanGate,
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

    /// <summary>What happened to one candidate.</summary>
    private enum CandidateOutcome
    {
        /// <summary>Posted, but only in chats the bot no longer manages: no longer a user.</summary>
        NoLongerAUser,
        /// <summary>Profile scanning is disabled in every active managed chat the user posted in.</summary>
        ScanningDisabled,
        /// <summary>The scan gate turned the user down (trusted, admin, banned, bot, excluded).</summary>
        Ineligible,
        /// <summary>A scan was written (full or name-only).</summary>
        Scanned,
        /// <summary>The scan ran but wrote nothing (skip reason set).</summary>
        NothingWritten,
        /// <summary>The chat lookup or the scan threw.</summary>
        Failed
    }

    /// <summary>Outcomes that take a batch slot and are throttled: the user was sent to a scan, or that failed.</summary>
    private static bool IsAttempt(CandidateOutcome outcome) =>
        outcome is CandidateOutcome.Scanned or CandidateOutcome.NothingWritten or CandidateOutcome.Failed;

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
            // The settings form allows 1 or more; a stored value below that is read as 1.
            var retryLimit = Math.Max(1, settings.NameOnlyRetryLimit);
            var rescanAfter = TimeSpanUtilities.ParseDurationOrDefault(settings.RescanAfter, TimeSpan.FromDays(7));
            var cutoff = DateTimeOffset.UtcNow - rescanAfter;

            logger.LogInformation(
                "Profile rescan: starting batch (size={BatchSize}, rescanAfter={RescanAfter}, cutoff={Cutoff}, nameOnlyRetryLimit={RetryLimit})",
                batchSize, settings.RescanAfter, cutoff, retryLimit);

            // One ordered candidate list, capped, rather than offset pages: a scan changes the user's
            // profile_scanned_at and scan rows, so offset pages would shift under the loop.
            var candidateCap = batchSize * CandidatesPerBatchSlot;
            var userIds = await userRepository.GetUsersWithIncompleteScansAsync(
                candidateCap, cutoff, retryLimit, cancellationToken);

            if (userIds.Count == 0)
            {
                logger.LogInformation("Profile rescan: no incomplete scans to retry");
                success = true;
                return;
            }

            logger.LogInformation("Profile rescan: found {Count} candidates", userIds.Count);

            // One lookup for the candidates; ResolveManyAsync keeps the requested order
            var users = await identityService.ResolveManyAsync(userIds, cancellationToken);

            var outcomes = new Dictionary<CandidateOutcome, int>();
            var examined = 0;
            var attempted = 0;
            foreach (var user in users)
            {
                // Batch slots go to scan attempts only
                if (attempted >= batchSize)
                    break;

                examined++;
                var outcome = await RescanAsync(user, cancellationToken);
                outcomes[outcome] = outcomes.GetValueOrDefault(outcome) + 1;

                // Nothing was written, whether skipped, turned down or failed: a never-scanned user
                // waits Re-Scan After before the job considers them again, so skipped users rotate
                // instead of holding the head of the candidate list (a no-op for scanned users)
                if (outcome is not CandidateOutcome.Scanned)
                    await RecordAttemptAsync(user.Id, cancellationToken);

                if (IsAttempt(outcome))
                {
                    attempted++;
                    // Throttle to avoid Telegram FLOOD_WAIT rate limits
                    await Task.Delay(ScanThrottle, cancellationToken);
                }
            }

            int Count(CandidateOutcome outcome) => outcomes.GetValueOrDefault(outcome);

            if (Count(CandidateOutcome.ScanningDisabled) > 0)
                logger.LogInformation(
                    "Profile rescan: skipped {Count} users, scanning disabled in every chat", Count(CandidateOutcome.ScanningDisabled));

            if (Count(CandidateOutcome.NoLongerAUser) > 0)
                logger.LogInformation(
                    "Profile rescan: skipped {Count} users who only posted in chats no longer managed", Count(CandidateOutcome.NoLongerAUser));

            if (Count(CandidateOutcome.Ineligible) > 0)
                logger.LogInformation(
                    "Profile rescan: skipped {Count} users the scan gate turned down", Count(CandidateOutcome.Ineligible));

            if (attempted == 0)
                logger.LogInformation("Profile rescan: examined {Examined} candidates but attempted no scans", examined);

            logger.LogInformation("Profile rescan: completed {Scanned}/{Attempted} users ({Skipped} skipped)",
                Count(CandidateOutcome.Scanned), attempted, Count(CandidateOutcome.NothingWritten));
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

    /// <summary>
    /// Records that the job considered the user and wrote nothing. Runs after the attempt, so a
    /// failure here is logged and the batch goes on.
    /// </summary>
    private async Task RecordAttemptAsync(long userId, CancellationToken cancellationToken)
    {
        try
        {
            await userRepository.RecordScanAttemptAsync(userId, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Profile rescan: could not record the scan attempt for user {UserId}, continuing batch", userId);
        }
    }

    /// <summary>
    /// Picks the chat to scan the user for and sends the user through the scan gate. The chat is the
    /// most recently active of their active managed chats with profile scanning enabled (it targets
    /// alerts and supplies thresholds). A user with no active managed chat is no longer a user when
    /// they have message history elsewhere; one who never posted follows the global config (no chat).
    /// </summary>
    private async Task<CandidateOutcome> RescanAsync(UserIdentity user, CancellationToken cancellationToken)
    {
        try
        {
            ChatIdentity? scanChat = null;
            var chats = await userRepository.GetChatsForUserAsync(user.Id, cancellationToken);
            if (chats.Count == 0)
            {
                if (await userRepository.HasMessageHistoryAsync(user.Id, cancellationToken))
                {
                    logger.LogDebug("Profile rescan: user {UserId} only posted in chats no longer managed, skipping", user.Id);
                    return CandidateOutcome.NoLongerAUser;
                }

                if (!await scanGate.IsScanningEnabledAsync(0, cancellationToken))
                    return ScanningDisabled();
            }
            else
            {
                foreach (var chat in chats)
                {
                    if (await scanGate.IsScanningEnabledAsync(chat.Id, cancellationToken))
                    {
                        scanChat = chat;
                        break;
                    }
                }

                if (scanChat is null)
                    return ScanningDisabled();
            }

            var result = await scanGate.ScanIfEligibleAsync(user, scanChat, ProfileScanTrigger.Rescan, cancellationToken);

            // A name-only scan counts as scanned; a skip reason means nothing was written.
            return result is null ? CandidateOutcome.Ineligible
                : result.SkipReason is null ? CandidateOutcome.Scanned
                : CandidateOutcome.NothingWritten;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Profile rescan: failed to scan user {UserId}, continuing batch", user.Id);
            return CandidateOutcome.Failed;
        }

        CandidateOutcome ScanningDisabled()
        {
            logger.LogDebug("Profile rescan: scanning disabled in every chat of user {UserId}, skipping", user.Id);
            return CandidateOutcome.ScanningDisabled;
        }
    }
}
