namespace TelegramGroupsAdmin.BackgroundJobs.Services;

/// <summary>
/// A one-slot wake-up signal decoupling the config writer (producer) from the
/// schedule-sync worker (consumer). Replaces the former concrete back-reference
/// (SetSyncService cast). Registered as a singleton so both sides share one instance.
/// </summary>
public interface IScheduleResyncSignal
{
    /// <summary>
    /// Request a re-sync. Coalescing: multiple calls before the next WaitAsync
    /// collapse into a single pending wake-up.
    /// </summary>
    void RequestResync();

    /// <summary>Await the next re-sync request (or cancellation).</summary>
    Task WaitAsync(CancellationToken cancellationToken);
}

public sealed class ScheduleResyncSignal : IScheduleResyncSignal, IDisposable
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void RequestResync()
    {
        // CurrentCount == 0 means no pending wake-up; release to signal one.
        // CurrentCount == 1 means a resync is already pending — skip (coalescing).
        if (_signal.CurrentCount == 0)
            _signal.Release();
    }

    public Task WaitAsync(CancellationToken cancellationToken) => _signal.WaitAsync(cancellationToken);

    public void Dispose() => _signal.Dispose();
}
