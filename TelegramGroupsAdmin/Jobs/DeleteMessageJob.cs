using System.Diagnostics;
using Telegram.Bot;
using TelegramGroupsAdmin.Core.Telemetry;
using TelegramGroupsAdmin.Telegram.Abstractions.Services;
using TelegramGroupsAdmin.Telegram.Abstractions.Jobs;
using TelegramGroupsAdmin.Telegram.Services;

namespace TelegramGroupsAdmin.Jobs;

/// <summary>
/// Job logic to handle delayed message deletion
/// Replaces fire-and-forget Task.Run in WelcomeService (C1 critical issue)
/// Phase 4.4: Welcome system
/// </summary>
public class DeleteMessageJobLogic(
    ILogger<DeleteMessageJobLogic> logger,
    TelegramBotClientFactory botClientFactory,
    TelegramConfigLoader configLoader)
{
    private readonly ILogger<DeleteMessageJobLogic> _logger = logger;
    private readonly TelegramBotClientFactory _botClientFactory = botClientFactory;
    private readonly TelegramConfigLoader _configLoader = configLoader;

    /// <summary>
    /// Execute delayed message deletion
    /// </summary>
    public async Task ExecuteAsync(DeleteMessagePayload payload, CancellationToken cancellationToken)
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
