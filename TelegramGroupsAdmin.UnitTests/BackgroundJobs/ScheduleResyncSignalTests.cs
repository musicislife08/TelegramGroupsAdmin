using TelegramGroupsAdmin.BackgroundJobs.Services;

namespace TelegramGroupsAdmin.UnitTests.BackgroundJobs;

[TestFixture]
public class ScheduleResyncSignalTests
{
    [Test]
    public async Task RequestResync_ThenWaitAsync_Completes()
    {
        using var signal = new ScheduleResyncSignal();

        signal.RequestResync();

        await signal.WaitAsync(CancellationToken.None); // must complete without blocking
        Assert.Pass();
    }

    [Test]
    public async Task RequestResync_CalledTwiceBeforeWait_Coalesces()
    {
        using var signal = new ScheduleResyncSignal();

        signal.RequestResync();
        signal.RequestResync(); // must not throw; collapses into one pending wake

        await signal.WaitAsync(CancellationToken.None); // first wait completes

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        Assert.ThrowsAsync<OperationCanceledException>(async () => await signal.WaitAsync(cts.Token));
    }

    [Test]
    public async Task RequestResync_ConcurrentCalls_NeverThrow()
    {
        using var signal = new ScheduleResyncSignal();
        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        var tasks = Enumerable.Range(0, 100).Select(_ => Task.Run(() =>
        {
            try { signal.RequestResync(); }
            catch (Exception ex) { exceptions.Add(ex); }
        })).ToArray();
        await Task.WhenAll(tasks);

        Assert.That(exceptions, Is.Empty, "Concurrent RequestResync must never throw");
    }
}
