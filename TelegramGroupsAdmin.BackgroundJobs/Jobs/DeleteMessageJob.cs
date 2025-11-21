using System.Text.Json;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Quartz;
using Telegram.Bot;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Core.Telemetry;
using TelegramGroupsAdmin.Telegram.Abstractions.Services;
using TelegramGroupsAdmin.Telegram.Abstractions.Jobs;
using TelegramGroupsAdmin.Telegram.Services;

namespace TelegramGroupsAdmin.BackgroundJobs.Jobs;

/// <summary>
/// Quartz.NET job to handle delayed message deletion
/// Replaces fire-and-forget Task.Run in WelcomeService (C1 critical issue)
/// Phase 4.4: Welcome system
/// </summary>
public class DeleteMessageJob(
    ILogger<DeleteMessageJob> logger,
    TelegramBotClientFactory botClientFactory,
    TelegramConfigLoader configLoader) : IJob
{
    private readonly ILogger<DeleteMessageJob> _logger = logger;
    private readonly TelegramBotClientFactory _botClientFactory = botClientFactory;
    private readonly TelegramConfigLoader _configLoader = configLoader;

    /// <summary>
    /// Execute delayed message deletion (Quartz.NET entry point)
    /// </summary>
    public async Task Execute(IJobExecutionContext context)
    {
        // Extract payload from job data map (deserialize from JSON string)
        var payloadJson = context.JobDetail.JobDataMap.GetString(JobDataKeys.PayloadJson)
            ?? throw new InvalidOperationException("payload not found in job data");

        var payload = JsonSerializer.Deserialize<DeleteMessagePayload>(payloadJson)
            ?? throw new InvalidOperationException("Failed to deserialize DeleteMessagePayload");

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
            if (payload == null)
            {
                _logger.LogError("DeleteMessageJobLogic received null payload");
                return;
            }

            _logger.LogDebug(
                "Deleting message {MessageId} in chat {ChatId} (reason: {Reason})",
                payload.MessageId,
                payload.ChatId,
                payload.Reason);

            // Load bot config from database
            var botToken = await _configLoader.LoadConfigAsync();

            // Get bot client from factory
            var botClient = _botClientFactory.GetOrCreate(botToken);

            await botClient.DeleteMessage(
                chatId: payload.ChatId,
                messageId: payload.MessageId,
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Deleted message {MessageId} in chat {ChatId} (reason: {Reason})",
                payload.MessageId,
                payload.ChatId,
                payload.Reason);

            success = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to delete message {MessageId} in chat {ChatId}",
                payload?.MessageId,
                payload?.ChatId);
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
