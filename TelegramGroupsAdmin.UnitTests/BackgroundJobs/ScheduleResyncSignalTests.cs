using TelegramGroupsAdmin.BackgroundJobs.Services;

namespace TelegramGroupsAdmin.UnitTests.BackgroundJobs;

[TestFixture]
public class ScheduleResyncSignalTests
{
    [Test]
    public async Task RequestResync_ThenWaitAsync_Completes()
    {
        var signal = new ScheduleResyncSignal();

        signal.RequestResync();

        await signal.WaitAsync(CancellationToken.None); // must complete without blocking
        Assert.Pass();
    }

    [Test]
    public async Task RequestResync_CalledTwiceBeforeWait_Coalesces()
    {
        var signal = new ScheduleResyncSignal();

        signal.RequestResync();
        signal.RequestResync(); // must not throw; collapses into one pending wake

        await signal.WaitAsync(CancellationToken.None); // first wait completes

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        Assert.ThrowsAsync<OperationCanceledException>(async () => await signal.WaitAsync(cts.Token));
    }
}
