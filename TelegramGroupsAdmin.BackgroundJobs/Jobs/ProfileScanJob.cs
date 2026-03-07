using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Quartz;
using TelegramGroupsAdmin.BackgroundJobs.Helpers;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Telemetry;
using TelegramGroupsAdmin.Core.JobPayloads;
using TelegramGroupsAdmin.Telegram.Services.UserApi;

namespace TelegramGroupsAdmin.BackgroundJobs.Jobs;

/// <summary>
/// On-demand Quartz job that runs a full User API profile scan for a user.
/// Triggered by: on-message profile diff detection, manual re-scan from UI.
/// Deduplicated by userId — only one scan per user at a time.
/// </summary>
public class ProfileScanJob(
    ILogger<ProfileScanJob> logger,
    IProfileScanService profileScanService,
    ITelegramSessionManager sessionManager) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var payload = await JobPayloadHelper.TryGetPayloadAsync<ProfileScanPayload>(context, logger);
        if (payload == null) return;

        await ExecuteAsync(payload, context.CancellationToken);
    }

    private async Task ExecuteAsync(ProfileScanPayload payload, CancellationToken cancellationToken)
    {
        const string jobName = "ProfileScan";
        var startTimestamp = Stopwatch.GetTimestamp();
        var success = false;

        try
        {
            // Skip if no User API session available
            if (!await sessionManager.HasAnyActiveSessionAsync(cancellationToken))
            {
                logger.LogDebug("No User API session available, skipping profile scan for user {UserId}",
                    payload.UserId);
                success = true;
                return;
            }

            var user = UserIdentity.FromId(payload.UserId);
            var chat = payload.ChatId.HasValue ? ChatIdentity.FromId(payload.ChatId.Value) : null;

            logger.LogDebug("Starting background profile scan for user {UserId} (chat: {ChatId})",
                payload.UserId, payload.ChatId);

            var result = await profileScanService.ScanUserProfileAsync(user, chat, cancellationToken);

            logger.LogInformation(
                "Background profile scan completed for user {UserId}: score {Score}, outcome {Outcome}",
                payload.UserId, result.Score, result.Outcome);

            success = true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to run profile scan for user {UserId}",
                payload.UserId);
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
