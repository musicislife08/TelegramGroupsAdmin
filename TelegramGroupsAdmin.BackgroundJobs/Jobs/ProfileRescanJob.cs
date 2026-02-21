using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;
using TelegramGroupsAdmin.BackgroundJobs.Services;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Models.BackgroundJobSettings;
using TelegramGroupsAdmin.Core.Telemetry;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Data;
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
    IServiceScopeFactory scopeFactory,
    IBackgroundJobConfigService jobConfigService,
    ITelegramSessionManager sessionManager) : IJob
{
    private readonly ILogger<ProfileRescanJob> _logger = logger;
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly IBackgroundJobConfigService _jobConfigService = jobConfigService;
    private readonly ITelegramSessionManager _sessionManager = sessionManager;

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

            // Query eligible users
            await using var dbContext = await GetDbContextAsync(cancellationToken);
            var userIds = await dbContext.TelegramUsers
                .Where(u => !u.IsBanned && !u.IsBot && !u.IsTrusted)
                .Where(u => u.ProfileScannedAt == null || u.ProfileScannedAt < cutoff)
                .OrderBy(u => u.ProfileScannedAt) // NULLS FIRST is PostgreSQL default for ASC
                .Take(batchSize)
                .Select(u => u.TelegramUserId)
                .ToListAsync(cancellationToken);

            if (userIds.Count == 0)
            {
                _logger.LogInformation("Profile rescan: no eligible users found");
                success = true;
                return;
            }

            _logger.LogInformation("Profile rescan: found {Count} users to scan", userIds.Count);

            var scanned = 0;
            foreach (var userId in userIds)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var profileScanService = scope.ServiceProvider.GetRequiredService<IProfileScanService>();

                    await profileScanService.ScanUserProfileAsync(
                        UserIdentity.FromId(userId),
                        triggeringChat: null,
                        cancellationToken);

                    scanned++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Profile rescan: failed to scan user {UserId}, continuing batch", userId);
                }
            }

            _logger.LogInformation("Profile rescan: completed {Scanned}/{Total} users", scanned, userIds.Count);
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

    private async Task<AppDbContext> GetDbContextAsync(CancellationToken ct)
    {
        var scope = _scopeFactory.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        return await factory.CreateDbContextAsync(ct);
    }
}
