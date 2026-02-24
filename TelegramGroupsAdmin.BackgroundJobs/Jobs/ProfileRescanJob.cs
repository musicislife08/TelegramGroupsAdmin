using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Quartz;
using TelegramGroupsAdmin.BackgroundJobs.Services;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Models.BackgroundJobSettings;
using TelegramGroupsAdmin.Core.Telemetry;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.UserApi;

namespace TelegramGroupsAdmin.BackgroundJobs.Jobs;

/// <summary>
/// Periodic cron job that re-scans user profiles to detect changes.
/// Queries users ordered by profile_scanned_at ASC NULLS FIRST (never-scanned first),
/// filters out banned/bot/trusted users, and processes a configurable batch size.
/// </summary>
[DisallowConcurrentExecution]
public class ProfileRescanJob(
    ILogger<ProfileRescanJob> logger,
    IBackgroundJobConfigService jobConfigService,
    ITelegramSessionManager sessionManager,
    ITelegramUserRepository userRepository,
    IProfileScanService profileScanService) : IJob
{
    private readonly ILogger<ProfileRescanJob> _logger = logger;
    private readonly IBackgroundJobConfigService _jobConfigService = jobConfigService;
    private readonly ITelegramSessionManager _sessionManager = sessionManager;
    private readonly ITelegramUserRepository _userRepository = userRepository;
    private readonly IProfileScanService _profileScanService = profileScanService;

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
            // Check User API availability first
            if (!await _sessionManager.HasAnyActiveSessionAsync(cancellationToken))
            {
                _logger.LogInformation("Profile rescan: no User API session available, skipping batch");
                success = true;
                return;
            }

            // Load job-specific settings
            var jobConfig = await _jobConfigService.GetJobConfigAsync(
                BackgroundJobNames.ProfileRescan, cancellationToken);
            var settings = jobConfig?.ProfileRescan ?? new();
            var batchSize = settings.BatchSize;
            var rescanAfter = TimeSpanUtilities.ParseDurationOrDefault(settings.RescanAfter, TimeSpan.FromDays(7));
            var cutoff = DateTimeOffset.UtcNow - rescanAfter;

            _logger.LogInformation(
                "Profile rescan: starting batch (size={BatchSize}, rescanAfter={RescanAfter}, cutoff={Cutoff})",
                batchSize, settings.RescanAfter, cutoff);

            // Query eligible users via repository
            var userIds = await _userRepository.GetEligibleUsersForRescanAsync(batchSize, cutoff, cancellationToken);

            if (userIds.Count == 0)
            {
                _logger.LogInformation("Profile rescan: no eligible users found");
                success = true;
                return;
            }

            _logger.LogInformation("Profile rescan: found {Count} users to scan", userIds.Count);

            var scanned = 0;
            var skipped = 0;
            var aborted = false;
            foreach (var userId in userIds)
            {
                try
                {
                    // Look up the user's most recently active chat for alert/notification targeting
                    var chat = await _userRepository.GetFirstChatForUserAsync(userId, cancellationToken);

                    var result = await _profileScanService.ScanUserProfileAsync(
                        UserIdentity.FromId(userId),
                        triggeringChat: chat,
                        cancellationToken);

                    // Session lost mid-batch — no point hammering reconnect for every remaining user
                    if (result.SkipReason is not null && result.SkipReason.Contains("No User API session", StringComparison.Ordinal))
                    {
                        aborted = true;
                        break;
                    }

                    if (result.SkipReason is null)
                        scanned++;
                    else
                        skipped++;

                    // Throttle to avoid Telegram FLOOD_WAIT rate limits
                    await Task.Delay(500, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Profile rescan: failed to scan user {UserId}, continuing batch", userId);
                }
            }

            if (aborted)
                _logger.LogWarning("Profile rescan: aborted — User API session unavailable ({Scanned} scanned, {Remaining} remaining)",
                    scanned, userIds.Count - scanned - skipped);
            else
                _logger.LogInformation("Profile rescan: completed {Scanned}/{Total} users ({Skipped} skipped)",
                    scanned, userIds.Count, skipped);
            success = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Profile rescan batch failed");
            throw; // Re-throw for Quartz retry
        }
        finally
        {
            var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

            var tags = new TagList
            {
                { "job_name", jobName },
                { "status", success ? "success" : "failure" }
            };

            TelemetryConstants.JobExecutions.Add(1, tags);
            TelemetryConstants.JobDuration.Record(elapsedMs, new TagList { { "job_name", jobName } });
        }
    }
}
