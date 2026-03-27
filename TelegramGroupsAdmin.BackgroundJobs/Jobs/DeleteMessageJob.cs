using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Quartz;
using TelegramGroupsAdmin.BackgroundJobs.Helpers;
using TelegramGroupsAdmin.BackgroundJobs.Metrics;
using TelegramGroupsAdmin.Core.JobPayloads;
using TelegramGroupsAdmin.Telegram.Services.Bot;

namespace TelegramGroupsAdmin.BackgroundJobs.Jobs;

/// <summary>
/// Quartz.NET job to handle delayed message deletion
/// Replaces fire-and-forget Task.Run in WelcomeService (C1 critical issue)
/// Phase 4.4: Welcome system
/// </summary>
public class DeleteMessageJob(
    ILogger<DeleteMessageJob> logger,
    IBotMessageService messageService,
    JobMetrics jobMetrics) : IJob
{
    /// <summary>
    /// Execute delayed message deletion (Quartz.NET entry point)
    /// </summary>
    public async Task Execute(IJobExecutionContext context)
    {
        var payload = await JobPayloadHelper.TryGetPayloadAsync<DeleteMessagePayload>(context, logger);
        if (payload == null) return;

        await ExecuteAsync(payload, context.CancellationToken);
    }

    /// <summary>
    /// Execute delayed message deletion (business logic)
    /// </summary>
    private async Task ExecuteAsync(DeleteMessagePayload payload, CancellationToken cancellationToken)
    {
        const string jobName = "DeleteMessage";
        var startTimestamp = Stopwatch.GetTimestamp();
        var success = false;

        try
        {
            logger.LogDebug(
                "Deleting message {MessageId} in chat {ChatId} (reason: {Reason})",
                payload.MessageId,
                payload.ChatId,
                payload.Reason);

            await messageService.DeleteAndMarkMessageAsync(
                chatId: payload.ChatId,
                messageId: payload.MessageId,
                deletionSource: payload.Reason ?? "scheduled_delete",
                cancellationToken: cancellationToken);

            logger.LogInformation(
                "Deleted message {MessageId} in chat {ChatId} (reason: {Reason})",
                payload.MessageId,
                payload.ChatId,
                payload.Reason);

            success = true;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to delete message {MessageId} in chat {ChatId}",
                payload?.MessageId,
                payload?.ChatId);
            throw; // Re-throw for retry logic and exception recording
        }
        finally
        {
            var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
            jobMetrics.RecordJobExecution(jobName, success, elapsedMs);
        }
    }
}
