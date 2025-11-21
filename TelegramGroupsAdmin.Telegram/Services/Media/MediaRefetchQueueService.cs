using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Services.Media;

/// <summary>
/// Singleton queue service for media refetch operations
/// Uses System.Threading.Channels for high-performance producer-consumer pattern
/// In-memory HashSet deduplication prevents duplicate downloads
/// </summary>
public class MediaRefetchQueueService : IMediaRefetchQueueService
{
    private readonly Channel<RefetchRequest> _channel;
    private readonly ConcurrentDictionary<string, bool> _inFlight;
    private readonly ILogger<MediaRefetchQueueService> _logger;

    public MediaRefetchQueueService(ILogger<MediaRefetchQueueService> logger)
    {
        _logger = logger;
        _inFlight = new ConcurrentDictionary<string, bool>();

        // Bounded channel with 1000 capacity, drop oldest on overflow
        _channel = Channel.CreateBounded<RefetchRequest>(new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });

        _logger.LogInformation("MediaRefetchQueueService initialized with 1000 capacity");
    }

    /// <summary>
    /// Get the channel reader for worker consumption
    /// </summary>
    internal ChannelReader<RefetchRequest> Reader => _channel.Reader;

    public async ValueTask<bool> EnqueueMediaAsync(long messageId, MediaType mediaType)
    {
        var request = new RefetchRequest
        {
            MessageId = messageId,
            MediaType = mediaType,
            Type = RefetchType.Media
        };

        var key = request.GetKey();

        // Deduplication: Only enqueue if not already in-flight
        if (!_inFlight.TryAdd(key, true))
        {
            _logger.LogDebug("Media already queued: {Key}", key);
            return false;
        }

        await _channel.Writer.WriteAsync(request);
        _logger.LogDebug("Enqueued media refetch: {MessageId} {MediaType}", messageId, mediaType);
        return true;
    }

    public async ValueTask<bool> EnqueueUserPhotoAsync(long userId)
    {
        var request = new RefetchRequest
        {
            UserId = userId,
            Type = RefetchType.UserPhoto
        };

        var key = request.GetKey();

        // Deduplication: Only enqueue if not already in-flight
        if (!_inFlight.TryAdd(key, true))
        {
            _logger.LogDebug("User photo already queued: {UserId}", userId);
            return false;
        }

        await _channel.Writer.WriteAsync(request);
        _logger.LogDebug("Enqueued user photo refetch: {UserId}", userId);
        return true;
    }

    public void MarkCompleted(RefetchRequest request)
    {
        var key = request.GetKey();
        _inFlight.TryRemove(key, out _);
        _logger.LogDebug("Marked refetch completed: {Key}", key);
    }

    public int GetQueueDepth()
    {
        return _inFlight.Count;
    }
}
