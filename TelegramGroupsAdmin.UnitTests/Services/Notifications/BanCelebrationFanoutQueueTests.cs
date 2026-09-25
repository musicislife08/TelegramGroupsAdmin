using Microsoft.Extensions.Logging.Abstractions;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Services.Notifications;
using TelegramGroupsAdmin.Telegram.Metrics;

namespace TelegramGroupsAdmin.UnitTests.Services.Notifications;

[TestFixture]
public class BanCelebrationFanoutQueueTests
{
    [Test]
    public async Task EnqueueAsync_BeyondCapacity_DropsOldestItem()
    {
        var queue = new BanCelebrationFanoutQueue(new PipelineMetrics(), NullLogger<BanCelebrationFanoutQueue>.Instance);

        for (var i = 0; i <= BanCelebrationFanoutQueue.Capacity; i++)
        {
            await queue.EnqueueAsync(new BanCelebrationFanoutItem(ChatIdentity.FromId(i), "c", i), CancellationToken.None);
        }

        Assert.That(queue.Reader.TryRead(out var first), Is.True);
        Assert.That(first!.GifId, Is.EqualTo(1), "item 0 should have been dropped");
    }
}
