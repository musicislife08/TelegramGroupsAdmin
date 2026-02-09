using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Quartz;
using TelegramGroupsAdmin.BackgroundJobs.Helpers;
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
        var payload = await JobPayloadHelper.TryGetPayloadAsync<SendChatNotificationPayload>(context, _logger);
        if (payload == null) return;

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
        var chat = payload.Chat;
        var chatDisplay = chat.ChatName ?? chat.Id.ToString();

        try
        {
            _logger.LogDebug(
                "Sending {EventType} notification for chat {ChatDisplay} ({ChatId}): {Subject}",
                payload.EventType,
                chatDisplay, chat.Id,
                payload.Subject);

            var results = await _notificationService.SendChatNotificationAsync(
                chatId: chat.Id,
                eventType: payload.EventType,
                subject: payload.Subject,
                message: payload.Message,
                reportId: payload.ReportId,
                photoPath: payload.PhotoPath,
                reportedUserId: payload.ReportedUserId,
                reportType: payload.ReportType,
                cancellationToken: cancellationToken);

            var successCount = results.Count(r => r.Value);
            var failureCount = results.Count(r => !r.Value);

            _logger.LogInformation(
                "Sent {EventType} notification for chat {ChatDisplay} ({ChatId}): {SuccessCount} delivered, {FailureCount} failed",
                payload.EventType,
                chatDisplay, chat.Id,
                successCount,
                failureCount);

            success = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to send {EventType} notification for chat {ChatDisplay} ({ChatId})",
                payload.EventType,
                chatDisplay, chat.Id);
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
