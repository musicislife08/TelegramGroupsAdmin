using System.Diagnostics;
using TickerQ.Utilities.Base;
using Telegram.Bot;
using TickerQ.Utilities.Models;
using TelegramGroupsAdmin.Core.Telemetry;
using TelegramGroupsAdmin.Telegram.Abstractions.Services;
using TelegramGroupsAdmin.Telegram.Abstractions.Jobs;
using TelegramGroupsAdmin.Telegram.Services;

namespace TelegramGroupsAdmin.Jobs;

/// <summary>
/// TickerQ job to handle delayed message deletion
/// Replaces fire-and-forget Task.Run in WelcomeService (C1 critical issue)
/// Phase 4.4: Welcome system
/// </summary>
public class DeleteMessageJob(
    ILogger<DeleteMessageJob> logger,
    TelegramBotClientFactory botClientFactory,
    TelegramConfigLoader configLoader)
{
    private readonly ILogger<DeleteMessageJob> _logger = logger;
    private readonly TelegramBotClientFactory _botClientFactory = botClientFactory;
    private readonly TelegramConfigLoader _configLoader = configLoader;

    /// <summary>
    /// Execute delayed message deletion
    /// Scheduled via TickerQ with configurable delay
    /// </summary>
    [TickerFunction(functionName: "DeleteMessage")]
    public async Task ExecuteAsync(TickerFunctionContext<DeleteMessagePayload> context, CancellationToken cancellationToken)
    {
        const string jobName = "DeleteMessage";
        var startTimestamp = Stopwatch.GetTimestamp();
        var success = false;

        try
        {
            var payload = context.Request;
            if (payload == null)
            {
                _logger.LogError("DeleteMessageJob received null payload");
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
                context.Request?.MessageId,
                context.Request?.ChatId);
            throw; // Re-throw to let TickerQ handle retry logic and record exception
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
