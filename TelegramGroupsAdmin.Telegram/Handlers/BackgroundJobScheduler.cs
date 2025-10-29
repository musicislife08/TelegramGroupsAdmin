using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Telegram.Abstractions.Jobs;

namespace TelegramGroupsAdmin.Telegram.Handlers;

/// <summary>
/// Centralized background job scheduling for Telegram message processing.
/// Encapsulates TickerQ job scheduling with type-safe methods and retry policies.
/// Note: File scanning is handled by FileScanningHandler (separate concern).
/// </summary>
public class BackgroundJobScheduler
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<BackgroundJobScheduler> _logger;

    public BackgroundJobScheduler(
        IServiceProvider serviceProvider,
        ILogger<BackgroundJobScheduler> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// Schedule a message for deletion via TickerQ.
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

        await TickerQUtilities.ScheduleJobAsync(
            _serviceProvider,
            _logger,
            "DeleteMessage",
            deletePayload,
            delaySeconds,
            retries: 0); // No retries for deletions (message may already be gone)
    }

    /// <summary>
    /// Schedule user photo fetch via TickerQ with 0s delay (instant execution with persistence/retry).
    /// Used to: populate user profile photos for message history UI.
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

        await TickerQUtilities.ScheduleJobAsync(
            _serviceProvider,
            _logger,
            "FetchUserPhoto",
            photoPayload,
            delaySeconds: 0,
            retries: 2); // Retry on transient failures (network issues, Telegram API errors)
    }
}
