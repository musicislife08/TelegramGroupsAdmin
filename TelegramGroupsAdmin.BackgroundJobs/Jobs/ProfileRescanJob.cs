using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Quartz;
using TelegramGroupsAdmin.BackgroundJobs.Metrics;
using TelegramGroupsAdmin.BackgroundJobs.Services;
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
/// the retry limit). Every scan falls back to name-only, so the job runs with or without a User API session.
/// A user is skipped only when profile scanning is disabled in every chat they are known in.
/// </summary>
[DisallowConcurrentExecution]
public class ProfileRescanJob(
    ILogger<ProfileRescanJob> logger,
    IBackgroundJobConfigService jobConfigService,
    IConfigService configService,
    ITelegramUserRepository userRepository,
    IProfileScanService profileScanService,
    IUserIdentityService identityService,
    JobMetrics jobMetrics) : IJob
{
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

            // Query eligible users via repository
            var userIds = await userRepository.GetEligibleUsersForRescanAsync(
                batchSize, cutoff, settings.NameOnlyRetryLimit, cancellationToken);

            if (userIds.Count == 0)
            {
                logger.LogInformation("Profile rescan: no eligible users found");
                success = true;
                return;
            }

            logger.LogInformation("Profile rescan: found {Count} users to scan", userIds.Count);

            // One lookup for the batch; ResolveManyAsync keeps the requested order
            var users = await identityService.ResolveManyAsync(userIds, cancellationToken);

            var scanned = 0;
            var skipped = 0;
            foreach (var user in users)
            {
                try
                {
                    // Scan for the user's most recently active chat with profile scanning enabled (it
                    // targets alerts and supplies thresholds). Skip only when every chat they are in has
                    // scanning disabled; with no known chat, the global config (chat 0) decides.
                    var (eligible, chat) = await FindScanChatAsync(user.Id, cancellationToken);
                    if (!eligible)
                    {
                        logger.LogDebug("Profile rescan: scanning disabled in every chat of user {UserId}, skipping",
                            user.Id);
                        skipped++;
                        continue;
                    }

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

            logger.LogInformation("Profile rescan: completed {Scanned}/{Total} users ({Skipped} skipped)",
                scanned, userIds.Count, skipped);
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
