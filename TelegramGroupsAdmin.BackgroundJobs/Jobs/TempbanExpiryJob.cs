using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Quartz;
using TelegramGroupsAdmin.BackgroundJobs.Helpers;
using TelegramGroupsAdmin.Core.Telemetry;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Moderation;
using TelegramGroupsAdmin.Core.JobPayloads;

namespace TelegramGroupsAdmin.BackgroundJobs.Jobs;

/// <summary>
/// Job logic to handle tempban expiry - completely removes user from "Removed users" list
/// Scheduled when tempban is issued, runs at expiry time
/// Calls UnbanChatMember() across all managed chats to allow invite link rejoining
/// Phase 4.6: Tempban with auto-unrestrict
/// </summary>
public class TempbanExpiryJob(
    ILogger<TempbanExpiryJob> logger,
    IDbContextFactory<AppDbContext> contextFactory,
    IBotModerationService moderationService) : IJob
{
    private readonly ILogger<TempbanExpiryJob> _logger = logger;
    private readonly IDbContextFactory<AppDbContext> _contextFactory = contextFactory;
    private readonly IBotModerationService _moderationService = moderationService;

    public async Task Execute(IJobExecutionContext context)
    {
        var payload = await JobPayloadHelper.TryGetPayloadAsync<TempbanExpiryJobPayload>(context, _logger);
        if (payload == null) return;

        await ExecuteAsync(payload, context.CancellationToken);
    }

    /// <summary>
    /// Execute tempban expiry - unban user from all managed chats
    /// Completely removes user from Telegram's "Removed users" list
    /// Allows user to use invite links to rejoin
    /// </summary>
    private async Task ExecuteAsync(TempbanExpiryJobPayload payload, CancellationToken cancellationToken)
    {
        const string jobName = "TempbanExpiry";
        var startTimestamp = Stopwatch.GetTimestamp();
        var success = false;

        try
        {
            _logger.LogInformation(
                "Processing tempban expiry for {User}. Reason: {Reason}, Expired at: {ExpiresAt}",
                payload.User.DisplayName,
                payload.Reason,
                payload.ExpiresAt);

            try
            {
                // Unban user across all managed chats via moderation service
                var result = await _moderationService.UnbanUserAsync(
                    new UnbanIntent
                    {
                        User = payload.User,
                        Executor = Core.Models.Actor.TempbanExpiry,
                        Reason = $"Tempban expired (original reason: {payload.Reason})",
                        RestoreTrust = false
                    },
                    cancellationToken);

                if (result.Success)
                {
                    _logger.LogInformation(
                        "Completed tempban expiry for {User}. Unbanned from {ChatsAffected} chats",
                        payload.User.DisplayName,
                        result.ChatsAffected);
                    success = true;
                }
                else
                {
                    _logger.LogWarning(
                        "Tempban expiry partially failed for {User}: {Error}",
                        payload.User.DisplayName,
                        result.ErrorMessage);
                    // Don't throw - partial success is acceptable for tempban expiry
                    success = true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to process tempban expiry for {User}",
                    payload.User.DisplayName);
                throw; // Re-throw for retry logic and exception recording
            }
        }
        finally
        {
            var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

            // Record metrics (using TagList to avoid boxing/allocations)
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
