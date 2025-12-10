using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Quartz;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Core.Telemetry;
using TelegramGroupsAdmin.Core.JobPayloads;

namespace TelegramGroupsAdmin.BackgroundJobs.Jobs;

/// <summary>
/// Quartz.NET job to send chat notifications via INotificationService
/// Replaces fire-and-forget pattern in ReportService for reliable delivery
/// </summary>
[DisallowConcurrentExecution]
public class SendChatNotificationJob(
    ILogger<SendChatNotificationJob> logger,
    INotificationService notificationService) : IJob
{
    private readonly ILogger<SendChatNotificationJob> _logger = logger;
    private readonly INotificationService _notificationService = notificationService;

    /// <summary>
    /// Execute notification delivery (Quartz.NET entry point)
    /// </summary>
    public async Task Execute(IJobExecutionContext context)
    {
        // Extract payload from job data map (deserialize from JSON string)
        var payloadJson = context.JobDetail.JobDataMap.GetString(JobDataKeys.PayloadJson)
            ?? throw new InvalidOperationException("payload not found in job data");

        var payload = JsonSerializer.Deserialize<SendChatNotificationPayload>(payloadJson)
            ?? throw new InvalidOperationException("Failed to deserialize SendChatNotificationPayload");

        await ExecuteAsync(payload, context.CancellationToken);
    }

    /// <summary>
    /// Execute notification delivery (business logic)
    /// </summary>
    private async Task ExecuteAsync(SendChatNotificationPayload payload, CancellationToken cancellationToken)
    {
        const string jobName = "SendChatNotification";
        var startTimestamp = Stopwatch.GetTimestamp();
        var success = false;

        try
        {
            _logger.LogDebug(
                "Sending {EventType} notification for chat {ChatId}: {Subject}",
                payload.EventType,
                payload.ChatId,
                payload.Subject);

            var results = await _notificationService.SendChatNotificationAsync(
                chatId: payload.ChatId,
                eventType: payload.EventType,
                subject: payload.Subject,
                message: payload.Message,
                ct: cancellationToken);

            var successCount = results.Count(r => r.Value);
            var failureCount = results.Count(r => !r.Value);

            _logger.LogInformation(
                "Sent {EventType} notification for chat {ChatId}: {SuccessCount} delivered, {FailureCount} failed",
                payload.EventType,
                payload.ChatId,
                successCount,
                failureCount);

            success = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to send {EventType} notification for chat {ChatId}",
                payload.EventType,
                payload.ChatId);
            throw; // Re-throw for retry logic and exception recording
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
