using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Core.JobPayloads;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Actions.Results;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Infrastructure;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Actions;

/// <summary>
/// Domain handler for ban operations (ban, temp-ban, unban).
/// Handles Telegram API calls across all managed chats and database updates.
/// Does NOT know about trust, warnings, or notifications (orchestrator composes those).
/// REFACTOR-5: Updates is_banned column on telegram_users table (source of truth).
/// </summary>
public class BanHandler : IBanHandler
{
    private readonly ICrossChatExecutor _crossChatExecutor;
    private readonly IJobScheduler _jobScheduler;
    private readonly ITelegramUserRepository _userRepository;
    private readonly ILogger<BanHandler> _logger;

    public BanHandler(
        ICrossChatExecutor crossChatExecutor,
        IJobScheduler jobScheduler,
        ITelegramUserRepository userRepository,
        ILogger<BanHandler> logger)
    {
        _crossChatExecutor = crossChatExecutor;
        _jobScheduler = jobScheduler;
        _userRepository = userRepository;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<BanResult> BanAsync(
        long userId,
        Actor executor,
        string? reason,
        long? triggeredByMessageId = null,
        CancellationToken cancellationToken = default)
    {
        // Fetch once for logging
        var user = await _userRepository.GetByTelegramIdAsync(userId, cancellationToken);

        _logger.LogDebug(
            "Executing ban for user {User} by {Executor}",
            user.ToLogDebug(userId), executor.GetDisplayText());

        try
        {
            var crossResult = await _crossChatExecutor.ExecuteAcrossChatsAsync(
                async (ops, chatId, token) => await ops.BanChatMemberAsync(chatId, userId, cancellationToken: token),
                "Ban",
                cancellationToken);

            // Update source of truth: is_banned column on telegram_users
            await _userRepository.SetBanStatusAsync(userId, isBanned: true, expiresAt: null, cancellationToken);

            _logger.LogInformation(
                "Ban completed for {User}: {Success} succeeded, {Failed} failed",
                user.ToLogInfo(userId), crossResult.SuccessCount, crossResult.FailCount);

            return BanResult.Succeeded(crossResult.SuccessCount, crossResult.FailCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute ban for user {User}", user.ToLogDebug(userId));
            return BanResult.Failed(ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<TempBanResult> TempBanAsync(
        long userId,
        Actor executor,
        TimeSpan duration,
        string? reason,
        long? triggeredByMessageId = null,
        CancellationToken cancellationToken = default)
    {
        // Fetch once for logging
        var user = await _userRepository.GetByTelegramIdAsync(userId, cancellationToken);

        _logger.LogDebug(
            "Executing temp ban for user {User} for {Duration} by {Executor}",
            user.ToLogDebug(userId), duration, executor.GetDisplayText());

        try
        {
            var expiresAt = DateTimeOffset.UtcNow.Add(duration);

            // Ban globally (permanent in Telegram, lifted by background job)
            var crossResult = await _crossChatExecutor.ExecuteAcrossChatsAsync(
                async (ops, chatId, token) => await ops.BanChatMemberAsync(chatId, userId, cancellationToken: token),
                "TempBan",
                cancellationToken);

            // Update source of truth: is_banned column with expiry
            await _userRepository.SetBanStatusAsync(userId, isBanned: true, expiresAt: expiresAt, cancellationToken);

            // Schedule automatic unban via Quartz.NET
            var payload = new TempbanExpiryJobPayload(
                UserId: userId,
                Reason: reason ?? "Temporary ban",
                ExpiresAt: expiresAt);

            var delaySeconds = (int)(expiresAt - DateTimeOffset.UtcNow).TotalSeconds;
            var jobId = await _jobScheduler.ScheduleJobAsync(
                "TempbanExpiry",
                payload,
                delaySeconds: delaySeconds,
                cancellationToken);

            _logger.LogInformation(
                "Temp ban completed for {User}: {Success} succeeded, {Failed} failed. " +
                "Expires at {ExpiresAt} (JobId: {JobId})",
                user.ToLogInfo(userId), crossResult.SuccessCount, crossResult.FailCount, expiresAt, jobId);

            return TempBanResult.Succeeded(crossResult.SuccessCount, expiresAt, crossResult.FailCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute temp ban for user {User}", user.ToLogDebug(userId));
            return TempBanResult.Failed(ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<UnbanResult> UnbanAsync(
        long userId,
        Actor executor,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        // Fetch once for logging
        var user = await _userRepository.GetByTelegramIdAsync(userId, cancellationToken);

        _logger.LogDebug(
            "Executing unban for user {User} by {Executor}",
            user.ToLogDebug(userId), executor.GetDisplayText());

        try
        {
            // Unban from all Telegram chats
            var crossResult = await _crossChatExecutor.ExecuteAcrossChatsAsync(
                async (ops, chatId, token) => await ops.UnbanChatMemberAsync(chatId, userId, cancellationToken: token),
                "Unban",
                cancellationToken);

            // Update source of truth: clear is_banned flag
            await _userRepository.SetBanStatusAsync(userId, isBanned: false, expiresAt: null, cancellationToken);

            _logger.LogInformation(
                "Unban completed for {User}: {Success} succeeded, {Failed} failed",
                user.ToLogInfo(userId), crossResult.SuccessCount, crossResult.FailCount);

            return UnbanResult.Succeeded(crossResult.SuccessCount, crossResult.FailCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute unban for user {User}", user.ToLogDebug(userId));
            return UnbanResult.Failed(ex.Message);
        }
    }
}
