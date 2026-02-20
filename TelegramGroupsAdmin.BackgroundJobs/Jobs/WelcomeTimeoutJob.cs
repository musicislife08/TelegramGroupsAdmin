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
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories;

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
    IBotMessageService messageService,
    IReportsRepository reportsRepository) : IJob
{
    private readonly ILogger<WelcomeTimeoutJob> _logger = logger;
    private readonly IDbContextFactory<AppDbContext> _contextFactory = contextFactory;
    private readonly IBotModerationService _moderationService = moderationService;
    private readonly IBotMessageService _messageService = messageService;
    private readonly IReportsRepository _reportsRepository = reportsRepository;

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
                "Processing welcome timeout for {User} in {Chat}",
                payload.User.DisplayName,
                payload.Chat.DisplayName);

            // Check if user has responded
            await using var dbContext = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var response = await dbContext.WelcomeResponses
                .Where(r => r.ChatId == payload.Chat.Id
                    && r.UserId == payload.User.Id
                    && r.WelcomeMessageId == payload.WelcomeMessageId)
                .FirstOrDefaultAsync(cancellationToken);

            if (response == null || (int)response.Response != (int)Data.Models.WelcomeResponseType.Pending)
            {
                _logger.LogInformation(
                    "User {User} already responded to welcome in {Chat}, skipping timeout",
                    payload.User.DisplayName,
                    payload.Chat.DisplayName);
                return;
            }

            _logger.LogInformation(
                "Welcome timeout: {User} did not respond in {Chat}",
                payload.User.DisplayName,
                payload.Chat.DisplayName);

            // Kick user for timeout
            try
            {
                await _moderationService.KickUserFromChatAsync(
                    new KickIntent
                    {
                        User = payload.User,
                        Chat = payload.Chat,
                        Executor = Core.Models.Actor.WelcomeFlow,
                        Reason = "Welcome timeout"
                    },
                    cancellationToken);

                _logger.LogInformation(
                    "Kicked {User} from {Chat} due to welcome timeout",
                    payload.User.DisplayName,
                    payload.Chat.DisplayName);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to kick {User} from {Chat}",
                    payload.User.DisplayName,
                    payload.Chat.DisplayName);
                // Continue to delete message and update response even if kick fails
            }

            // Delete welcome message
            try
            {
                await _messageService.DeleteAndMarkMessageAsync(
                    chatId: payload.Chat.Id,
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
                    payload.Chat.Id);
            }

            // Update response record
            response.Response = Data.Models.WelcomeResponseType.Timeout;
            response.RespondedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Recorded welcome timeout for {User} in {Chat}",
                payload.User.DisplayName,
                payload.Chat.DisplayName);

            // Clean up orphaned ProfileScanAlert reports for this user+chat
            await CleanupOrphanedProfileScanAlertsAsync(payload, cancellationToken);

            success = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to process welcome timeout for {User} in chat {ChatId}",
                payload?.User.DisplayName,
                payload?.Chat.Id);
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

    /// <summary>
    /// Close any pending ProfileScanAlert reports for this user in the timed-out chat.
    /// When a user times out, the profile review is moot — they've been kicked.
    /// </summary>
    private async Task CleanupOrphanedProfileScanAlertsAsync(
        WelcomeTimeoutPayload payload,
        CancellationToken cancellationToken)
    {
        try
        {
            var pendingAlerts = await _reportsRepository.GetPendingProfileScanAlertsForUserAsync(
                payload.User.Id, cancellationToken);

            var chatAlerts = pendingAlerts.Where(a => a.Chat.Id == payload.Chat.Id).ToList();

            foreach (var alert in chatAlerts)
            {
                await _reportsRepository.TryUpdateStatusAsync(
                    alert.Id,
                    ReportStatus.Reviewed,
                    reviewedBy: Actor.WelcomeFlow.GetDisplayText(),
                    actionTaken: "Auto-resolved: user timed out",
                    cancellationToken: cancellationToken);

                _logger.LogInformation(
                    "Auto-closed ProfileScanAlert #{ReportId} for {User} in {Chat} (welcome timeout)",
                    alert.Id, payload.User.DisplayName, payload.Chat.DisplayName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to clean up orphaned ProfileScanAlerts for {User} in {Chat} (non-fatal)",
                payload.User.DisplayName, payload.Chat.Id);
        }
    }
}
