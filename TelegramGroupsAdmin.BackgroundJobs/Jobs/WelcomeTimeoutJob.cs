using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Quartz;
using TelegramGroupsAdmin.BackgroundJobs.Helpers;
using TelegramGroupsAdmin.Core.Telemetry;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Moderation;
using TelegramGroupsAdmin.Core.JobPayloads;

namespace TelegramGroupsAdmin.BackgroundJobs.Jobs;

/// <summary>
/// Job logic to handle welcome message timeout
/// Replaces fire-and-forget Task.Run in WelcomeService (C1 critical issue)
/// Phase 4.4: Welcome system
/// </summary>
public class WelcomeTimeoutJob(
    ILogger<WelcomeTimeoutJob> logger,
    IDbContextFactory<AppDbContext> contextFactory,
    IBotModerationService moderationService,
    IBotMessageService messageService) : IJob
{
    private readonly ILogger<WelcomeTimeoutJob> _logger = logger;
    private readonly IDbContextFactory<AppDbContext> _contextFactory = contextFactory;
    private readonly IBotModerationService _moderationService = moderationService;
    private readonly IBotMessageService _messageService = messageService;

    /// <summary>
    /// Quartz.NET entry point - extracts payload and delegates to ExecuteAsync
    /// </summary>
    public async Task Execute(IJobExecutionContext context)
    {
        var payload = await JobPayloadHelper.TryGetPayloadAsync<WelcomeTimeoutPayload>(context, _logger);
        if (payload == null) return;

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
            _logger.LogInformation(
                "Processing welcome timeout for user {UserId} in chat {ChatId}",
                payload.UserId,
                payload.ChatId);

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

            // Kick user for timeout
            try
            {
                await _moderationService.KickUserFromChatAsync(
                    new KickIntent
                    {
                        User = UserIdentity.FromId(payload.UserId),
                        Chat = ChatIdentity.FromId(payload.ChatId),
                        Executor = Core.Models.Actor.WelcomeFlow,
                        Reason = "Welcome timeout"
                    },
                    cancellationToken);

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
                await _messageService.DeleteAndMarkMessageAsync(
                    chatId: payload.ChatId,
                    messageId: payload.WelcomeMessageId,
                    deletionSource: "welcome_timeout",
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
