using TelegramGroupsAdmin.BackgroundJobs.Services.Backup;

namespace TelegramGroupsAdmin.UnitTests.BackgroundJobs.Services.Backup;

/// <summary>
/// Tests for <see cref="BackupFileLock"/>, which serialises everything that writes backup files or
/// changes the passphrase they are written under.
/// </summary>
[TestFixture]
public class BackupFileLockTests
{
    [Test]
    public async Task SecondAcquire_WaitsUntilTheFirstIsReleased()
    {
        var fileLock = new BackupFileLock();
        var first = await fileLock.AcquireAsync();

        var second = fileLock.AcquireAsync();
        await Task.Delay(100);
        Assert.That(second.IsCompleted, Is.False, "the lock is held, so a second holder must wait");

        first.Dispose();
        using var acquired = await second.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(second.IsCompletedSuccessfully, Is.True);
    }

    [Test]
    public async Task Acquire_HonoursCancellation()
    {
        var fileLock = new BackupFileLock();
        using var held = await fileLock.AcquireAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        Assert.That(async () => await fileLock.AcquireAsync(cts.Token), Throws.InstanceOf<OperationCanceledException>());
    }
}
