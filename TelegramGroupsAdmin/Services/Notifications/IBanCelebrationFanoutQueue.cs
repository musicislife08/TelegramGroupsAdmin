using System.Threading.Channels;

namespace TelegramGroupsAdmin.Services.Notifications;

internal interface IBanCelebrationFanoutQueue
{
    ValueTask EnqueueAsync(BanCelebrationFanoutItem item, CancellationToken ct);

    ChannelReader<BanCelebrationFanoutItem> Reader { get; }
}
