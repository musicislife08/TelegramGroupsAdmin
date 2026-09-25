using System.Threading.Channels;
using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Telegram.Metrics;

namespace TelegramGroupsAdmin.Services.Notifications;

/// <summary>
/// Singleton bounded, in-memory queue of celebrations awaiting subscriber fan-out. Drops the
/// oldest item when full — a minutes-old celebration during a raid isn't worth delivering — and
/// loses in-flight items on restart, which is acceptable for GIFs.
/// </summary>
internal sealed class BanCelebrationFanoutQueue : IBanCelebrationFanoutQueue
{
    public const int Capacity = 100;

    private readonly Channel<BanCelebrationFanoutItem> _channel;

    public BanCelebrationFanoutQueue(PipelineMetrics pipelineMetrics, ILogger<BanCelebrationFanoutQueue> logger)
    {
        _channel = Channel.CreateBounded<BanCelebrationFanoutItem>(
            new BoundedChannelOptions(Capacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true
            },
            dropped =>
            {
                pipelineMetrics.RecordBanCelebrationDm("dropped");
                logger.LogWarning(
                    "Ban celebration fan-out queue full; dropped the oldest celebration for {Chat}",
                    dropped.Chat.ToLogDebug());
            });
    }

    public ChannelReader<BanCelebrationFanoutItem> Reader => _channel.Reader;

    public ValueTask EnqueueAsync(BanCelebrationFanoutItem item, CancellationToken ct) =>
        _channel.Writer.WriteAsync(item, ct);
}
