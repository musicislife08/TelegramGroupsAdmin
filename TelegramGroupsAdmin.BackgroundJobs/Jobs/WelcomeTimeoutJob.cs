using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Quartz;
using TelegramGroupsAdmin.BackgroundJobs.Helpers;
using TelegramGroupsAdmin.BackgroundJobs.Metrics;
using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Core.JobPayloads;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Telegram.Metrics;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Moderation;

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
    IExamSessionRepository examSessionRepository,
    JobMetrics jobMetrics,
    WelcomeMetrics welcomeMetrics) : IJob
{

    /// <summary>
    /// Quartz.NET entry point - extracts payload and delegates to ExecuteAsync
    /// </summary>
    public async Task Execute(IJobExecutionContext context)
    {
        var payload = await JobPayloadHelper.TryGetPayloadAsync<WelcomeTimeoutPayload>(context, logger);
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
            logger.LogInformation(
                "Processing welcome timeout for {User} in {Chat}",
                payload.User.ToLogInfo(),
                payload.Chat.ToLogInfo());

            // Check if user has responded
            await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
            var response = await dbContext.WelcomeResponses
                .Where(r => r.ChatId == payload.Chat.Id
                    && r.UserId == payload.User.Id
                    && r.WelcomeMessageId == payload.WelcomeMessageId)
                .FirstOrDefaultAsync(cancellationToken);

            if (response == null || (int)response.Response != (int)Data.Models.WelcomeResponseType.Pending)
            {
                logger.LogDebug(
                    "User {User} already handled in {Chat}, ensuring welcome message cleanup",
                    payload.User.ToLogDebug(),
                    payload.Chat.ToLogDebug());

                try
                {
                    await messageService.DeleteAndMarkMessageAsync(
                        chatId: payload.Chat.Id,
                        messageId: payload.WelcomeMessageId,
                        deletionSource: "welcome_timeout_cleanup",
                        cancellationToken: cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex,
                        "Welcome message {MessageId} already cleaned up or not found",
                        payload.WelcomeMessageId);
                }

                return;
            }

            // Check if user has an active exam session — let the exam flow handle timeout
            if (await examSessionRepository.HasActiveSessionAsync(payload.Chat.Id, payload.User.Id, cancellationToken))
            {
                logger.LogInformation(
                    "User {User} has active exam session in {Chat}, deferring to exam timeout",
                    payload.User.ToLogInfo(),
                    payload.Chat.ToLogInfo());
                return;
            }

            logger.LogInformation(
                "Welcome timeout: {User} did not respond in {Chat}",
                payload.User.ToLogInfo(),
                payload.Chat.ToLogInfo());

            // Kick user for timeout
            var kicked = false;
            try
            {
                var kickResult = await moderationService.KickUserFromChatAsync(
                    new KickIntent
                    {
                        User = payload.User,
                        Chat = payload.Chat,
                        Executor = Core.Models.Actor.WelcomeFlow,
                        Reason = "Welcome timeout"
                    },
                    cancellationToken);
                kicked = kickResult.Success;

                if (kicked)
                {
                    logger.LogInformation(
                        "Kicked {User} from {Chat} due to welcome timeout",
                        payload.User.ToLogInfo(),
                        payload.Chat.ToLogInfo());
                }
                else
                {
                    logger.LogWarning(
                        "Failed to kick {User} from {Chat}: {Error}",
                        payload.User.ToLogInfo(),
                        payload.Chat.ToLogInfo(),
                        kickResult.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Failed to kick {User} from {Chat}",
                    payload.User.ToLogInfo(),
                    payload.Chat.ToLogInfo());
                // Continue to update the response record even if kick fails
            }

            // The orchestrator deletes the welcome message as part of a SUCCESSFUL kick. On a
            // failed kick it never runs, so delete here — otherwise the message survives with
            // live Accept/Deny buttons while the record says Timeout, and the user could click
            // Accept to self-admit.
            if (!kicked)
            {
                try
                {
                    await messageService.DeleteAndMarkMessageAsync(
                        chatId: payload.Chat.Id,
                        messageId: payload.WelcomeMessageId,
                        deletionSource: "welcome_timeout_kick_failed",
                        cancellationToken: cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(
                        ex,
                        "Failed to delete welcome message {MessageId} in chat {ChatId}",
                        payload.WelcomeMessageId,
                        payload.Chat.Id);
                }
            }

            // Update response record
            response.Response = Data.Models.WelcomeResponseType.Timeout;
            response.RespondedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);

            welcomeMetrics.RecordWelcomeTimeout();
            welcomeMetrics.RecordWelcomeOutcome("timed_out", Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);

            logger.LogInformation(
                "Recorded welcome timeout for {User} in {Chat}",
                payload.User.ToLogInfo(),
                payload.Chat.ToLogInfo());

            success = true;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to process welcome timeout for {User} in {Chat}",
                payload?.User.ToLogDebug(),
                payload?.Chat.ToLogDebug());
            throw; // Re-throw for retry logic and exception recording
        }
        finally
        {
            var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
            jobMetrics.RecordJobExecution(jobName, success, elapsedMs);
        }
    }

}
