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

internal sealed class ScheduleResyncSignal : IScheduleResyncSignal, IDisposable
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void RequestResync()
    {
        // SemaphoreSlim(maxCount=1): Release() throws SemaphoreFullException when already
        // signaled. That means a resync is already pending — coalescing is the desired
        // behavior, so the exception is intentionally swallowed. (A CurrentCount check is a
        // TOCTOU race under concurrent callers, hence try/catch.)
        try
        {
            _signal.Release();
        }
        // slopwatch-ignore: SW003 Intentional empty catch: SemaphoreFullException means a resync is already pending, so coalescing the duplicate request is the desired behavior.
        catch (SemaphoreFullException)
        {
            // Intentional: resync already pending (coalescing).
        }
    }

    public Task WaitAsync(CancellationToken cancellationToken) => _signal.WaitAsync(cancellationToken);

    public void Dispose() => _signal.Dispose();
}
