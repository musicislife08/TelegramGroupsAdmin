using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Core.JobPayloads;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Actions.Intents;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Actions.Results;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Infrastructure;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Actions;

/// <summary>
/// Handles temp ban intents by banning users and scheduling automatic unban.
/// This is the domain expert for temporary bans - it owns the Telegram API and job scheduling.
/// </summary>
public class TempBanActionHandler : IActionHandler<TempBanIntent, TempBanResult>
{
    private readonly ICrossChatExecutor _crossChatExecutor;
    private readonly IJobScheduler _jobScheduler;
    private readonly ILogger<TempBanActionHandler> _logger;

    public TempBanActionHandler(
        ICrossChatExecutor crossChatExecutor,
        IJobScheduler jobScheduler,
        ILogger<TempBanActionHandler> logger)
    {
        _crossChatExecutor = crossChatExecutor;
        _jobScheduler = jobScheduler;
        _logger = logger;
    }

    public async Task<TempBanResult> HandleAsync(TempBanIntent intent, CancellationToken ct = default)
    {
        _logger.LogDebug(
            "Executing temp ban for user {UserId} for {Duration} by {Executor}",
            intent.UserId, intent.Duration, intent.Executor.GetDisplayText());

        try
        {
            var expiresAt = DateTimeOffset.UtcNow.Add(intent.Duration);

            // Ban globally (permanent in Telegram, lifted by background job)
            var crossResult = await _crossChatExecutor.ExecuteAcrossChatsAsync(
                async (ops, chatId, token) => await ops.BanChatMemberAsync(chatId, intent.UserId, ct: token),
                "TempBan",
                ct);

            // Schedule automatic unban via Quartz.NET
            var payload = new TempbanExpiryJobPayload(
                UserId: intent.UserId,
                Reason: intent.Reason,
                ExpiresAt: expiresAt);

            var delaySeconds = (int)(expiresAt - DateTimeOffset.UtcNow).TotalSeconds;
            var jobId = await _jobScheduler.ScheduleJobAsync(
                "TempbanExpiry",
                payload,
                delaySeconds: delaySeconds,
                ct);

            _logger.LogInformation(
                "Temp ban completed for user {UserId}: {Success} succeeded, {Failed} failed. " +
                "Expires at {ExpiresAt} (JobId: {JobId})",
                intent.UserId, crossResult.SuccessCount, crossResult.FailCount, expiresAt, jobId);

            return TempBanResult.Succeeded(crossResult.SuccessCount, expiresAt, crossResult.FailCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute temp ban for user {UserId}", intent.UserId);
            return TempBanResult.Failed(ex.Message);
        }
    }
}
