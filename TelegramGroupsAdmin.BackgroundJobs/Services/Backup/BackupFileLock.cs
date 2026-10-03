namespace TelegramGroupsAdmin.BackgroundJobs.Services.Backup;

/// <summary>
/// Serialises everything that writes backup files or changes the passphrase they are written under:
/// backup export, passphrase rotation, and the repair and delete steps of the rotation dialog.
/// Without it, a backup finished mid-rotation would be encrypted with the passphrase the rotation is
/// about to replace. Registered as a singleton; the app runs as a single instance.
/// </summary>
public sealed class BackupFileLock
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    /// <summary>
    /// Waits for the lock. Dispose the result to release it.
    /// </summary>
    public async Task<IDisposable> AcquireAsync(CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        return new Releaser(_semaphore);
    }

    private sealed class Releaser(SemaphoreSlim semaphore) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                semaphore.Release();
        }
    }
}
