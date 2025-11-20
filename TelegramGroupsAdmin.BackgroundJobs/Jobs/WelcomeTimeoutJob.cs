using System.Text.Json;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Quartz;
using Telegram.Bot;
using TelegramGroupsAdmin.Core.Telemetry;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Telegram.Abstractions.Services;
using TelegramGroupsAdmin.Telegram.Abstractions.Jobs;
using TelegramGroupsAdmin.Telegram.Services;

namespace TelegramGroupsAdmin.BackgroundJobs.Jobs;

/// <summary>
/// Job logic to handle welcome message timeout
/// Replaces fire-and-forget Task.Run in WelcomeService (C1 critical issue)
/// Phase 4.4: Welcome system
/// </summary>
public class WelcomeTimeoutJob(
    ILogger<WelcomeTimeoutJob> logger,
    IDbContextFactory<AppDbContext> contextFactory,
    TelegramBotClientFactory botClientFactory,
    TelegramConfigLoader configLoader) : IJob
{
    private readonly ILogger<WelcomeTimeoutJob> _logger = logger;
    private readonly IDbContextFactory<AppDbContext> _contextFactory = contextFactory;
    private readonly TelegramBotClientFactory _botClientFactory = botClientFactory;
    private readonly TelegramConfigLoader _configLoader = configLoader;

    /// <summary>
    /// Quartz.NET entry point - extracts payload and delegates to ExecuteAsync
    /// </summary>
    public async Task Execute(IJobExecutionContext context)
    {
        // Extract payload from job data map (deserialize from JSON string)
        var payloadJson = context.JobDetail.JobDataMap.GetString("payload")
            ?? throw new InvalidOperationException("payload not found in job data");

        var payload = JsonSerializer.Deserialize<WelcomeTimeoutPayload>(payloadJson)
            ?? throw new InvalidOperationException("Failed to deserialize WelcomeTimeoutPayload");

        await ExecuteAsync(payload, context.CancellationToken);
    }

    /// <summary>
    /// Execute welcome timeout - kicks user if they haven't responded
    /// Scheduled with configurable delay (default 60s)
    /// </summary>
    private async Task ExecuteAsync(WelcomeTimeoutPayload payload, CancellationToken cancellationToken)
    {
        const string jobName = "WelcomeTimeout";
        var startTimestamp = Stopwatch.GetTimestamp();
        var success = false;

        try
        {
            if (payload == null)
            {
                _logger.LogError("WelcomeTimeoutJob received null payload");
                return;
            }

            _logger.LogInformation(
                "Processing welcome timeout for user {UserId} in chat {ChatId}",
                payload.UserId,
                payload.ChatId);

            // Load bot config from database
            var botToken = await _configLoader.LoadConfigAsync();

            // Get bot client from factory
            var botClient = _botClientFactory.GetOrCreate(botToken);

            // Check if user has responded
            await using var dbContext = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var response = await dbContext.WelcomeResponses
                .Where(r => r.ChatId == payload.ChatId
                    && r.UserId == payload.UserId
                    && r.WelcomeMessageId == payload.WelcomeMessageId)
                .FirstOrDefaultAsync(cancellationToken);

            if (response == null || (int)response.Response != (int)Data.Models.WelcomeResponseType.Pending)
            {
                _logger.LogInformation(
                    "User {UserId} already responded to welcome in chat {ChatId}, skipping timeout",
                    payload.UserId,
                    payload.ChatId);
                return;
            }

            _logger.LogInformation(
                "Welcome timeout: User {UserId} did not respond in chat {ChatId}",
                payload.UserId,
                payload.ChatId);

            // Kick user for timeout (ban then immediately unban)
            try
            {
                await botClient.BanChatMember(
                    chatId: payload.ChatId,
                    userId: payload.UserId,
                    cancellationToken: cancellationToken);
                await botClient.UnbanChatMember(
                    chatId: payload.ChatId,
                    userId: payload.UserId,
                    cancellationToken: cancellationToken);

                _logger.LogInformation(
                    "Kicked user {UserId} from chat {ChatId} due to welcome timeout",
                    payload.UserId,
                    payload.ChatId);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to kick user {UserId} from chat {ChatId}",
                    payload.UserId,
                    payload.ChatId);
                // Continue to delete message and update response even if kick fails
            }

            // Delete welcome message
            try
            {
                await botClient.DeleteMessage(
                    chatId: payload.ChatId,
                    messageId: payload.WelcomeMessageId,
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to delete welcome message {MessageId} in chat {ChatId}",
                    payload.WelcomeMessageId,
                    payload.ChatId);
            }

            // Update response record
            response.Response = Data.Models.WelcomeResponseType.Timeout;
            response.RespondedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Recorded welcome timeout for user {UserId} in chat {ChatId}",
                payload.UserId,
                payload.ChatId);

            success = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to process welcome timeout for user {UserId} in chat {ChatId}",
                payload?.UserId,
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
