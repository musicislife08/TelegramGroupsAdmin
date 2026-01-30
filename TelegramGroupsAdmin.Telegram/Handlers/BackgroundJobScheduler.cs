using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Core.JobPayloads;
using static TelegramGroupsAdmin.Core.BackgroundJobs.DeduplicationKeys;

namespace TelegramGroupsAdmin.Telegram.Handlers;

/// <summary>
/// Centralized background job scheduling for Telegram message processing.
/// Encapsulates Quartz.NET job scheduling with type-safe methods and retry policies.
/// Note: File scanning is handled by FileScanningHandler (separate concern).
/// </summary>
public class BackgroundJobScheduler
{
    private readonly IJobScheduler _jobScheduler;
    private readonly ILogger<BackgroundJobScheduler> _logger;

    public BackgroundJobScheduler(
        IJobScheduler jobScheduler,
        ILogger<BackgroundJobScheduler> logger)
    {
        _jobScheduler = jobScheduler;
        _logger = logger;
    }

    /// <summary>
    /// Schedule a message for deletion via Quartz.NET.
    /// Used for: command cleanup, temporary messages, spam removal.
    /// </summary>
    public async Task ScheduleMessageDeleteAsync(
        long chatId,
        int messageId,
        int delaySeconds,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var deletePayload = new DeleteMessagePayload(
            chatId,
            messageId,
            reason
        );

        await _jobScheduler.ScheduleJobAsync(
            "DeleteMessage",
            deletePayload,
            delaySeconds,
            deduplicationKey: None,
            cancellationToken);
    }

    /// <summary>
    /// Schedule user photo fetch via Quartz.NET with 0s delay (instant execution with persistence/retry).
    /// Used to: populate user profile photos for message history UI.
    /// Deduplicated by userId - multiple messages from same user won't trigger multiple photo fetches.
    /// </summary>
    public async Task ScheduleUserPhotoFetchAsync(
        long messageId,
        long userId,
        CancellationToken cancellationToken = default)
    {
        var photoPayload = new FetchUserPhotoPayload(
            messageId,
            userId
        );

        await _jobScheduler.ScheduleJobAsync(
            "FetchUserPhoto",
            photoPayload,
            delaySeconds: 0,
            deduplicationKey: FetchUserPhoto(userId),
            cancellationToken);
    }
}
