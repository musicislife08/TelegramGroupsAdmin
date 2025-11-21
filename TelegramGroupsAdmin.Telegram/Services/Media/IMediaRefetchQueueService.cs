using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Services.Media;

/// <summary>
/// Queue service for media and user photo refetch operations
/// Singleton service with in-memory deduplication
/// </summary>
public interface IMediaRefetchQueueService
{
    /// <summary>
    /// Enqueue a media file for download
    /// Returns false if already queued (deduplication)
    /// </summary>
    ValueTask<bool> EnqueueMediaAsync(long messageId, MediaType mediaType);

    /// <summary>
    /// Enqueue a user photo for download
    /// Returns false if already queued (deduplication)
    /// </summary>
    ValueTask<bool> EnqueueUserPhotoAsync(long userId);

    /// <summary>
    /// Mark a refetch operation as completed (cleanup)
    /// </summary>
    void MarkCompleted(RefetchRequest request);

    /// <summary>
    /// Get the current queue depth (queued + in-progress items)
    /// </summary>
    int GetQueueDepth();
}
