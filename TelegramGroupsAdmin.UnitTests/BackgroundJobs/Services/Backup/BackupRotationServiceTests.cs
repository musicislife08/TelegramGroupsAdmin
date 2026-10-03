using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using TelegramGroupsAdmin.BackgroundJobs.Services.Backup;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;

namespace TelegramGroupsAdmin.UnitTests.BackgroundJobs.Services.Backup;

/// <summary>
/// Tests for <see cref="BackupRotationService"/>, the pre-check the rotation dialog runs: scanning a
/// backup directory, repairing damaged backups, and deleting the ones the user gives up on.
/// The rotator is a substitute; the directory holds empty files named for each case.
/// </summary>
[TestFixture]
public class BackupRotationServiceTests
{
    private const string StoredPassphrase = "stored-passphrase-12345";
    private const string OriginalPassphrase = "original-passphrase-67890";
    private const string UserId = "user-id";

    private IBackupArchiveRotator _rotator = null!;
    private IAuditService _auditService = null!;
    private BackupFileLock _fileLock = null!;
    private BackupRotationService _service = null!;
    private string _parent = null!;
    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _rotator = Substitute.For<IBackupArchiveRotator>();
        var passphraseService = Substitute.For<IPassphraseManagementService>();
        passphraseService.GetDecryptedPassphraseAsync().Returns(StoredPassphrase);
        _auditService = Substitute.For<IAuditService>();

        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IAuditService)).Returns(_auditService);
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(serviceProvider);
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);

        _fileLock = new BackupFileLock();
        _service = new BackupRotationService(_rotator, passphraseService, _fileLock, scopeFactory, NullLogger<BackupRotationService>.Instance);
        _parent = Directory.CreateTempSubdirectory("tga_rotation_service_").FullName;
        _directory = Directory.CreateDirectory(Path.Combine(_parent, "backups")).FullName;
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_parent, recursive: true);

    [Test]
    public async Task ScanAsync_MissingDirectory_ReturnsEmptyScan()
    {
        var scan = await _service.ScanAsync(Path.Combine(_parent, "does-not-exist"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(scan.RotatableCount, Is.Zero);
            Assert.That(scan.WrappedFiles, Is.Empty);
            Assert.That(scan.UnreadableFiles, Is.Empty);
        }
    }

    [Test]
    public async Task ScanAsync_GroupsFilesByState()
    {
        BackupFile("a.tar.gz", BackupFileState.Encrypted);
        BackupFile("b.tar.gz", BackupFileState.Wrapped);
        BackupFile("c.tar.gz", BackupFileState.Plain);
        BackupFile("d.tar.gz", BackupFileState.Unreadable);

        var scan = await _service.ScanAsync(_directory);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(scan.RotatableCount, Is.EqualTo(2));
            Assert.That(scan.WrappedFiles, Is.EqualTo(new[] { "b.tar.gz" }));
            Assert.That(scan.UnreadableFiles, Is.EqualTo(new[] { "d.tar.gz" }));
        }
    }

    [Test]
    public async Task RepairWrappedAsync_RepairsWhatTheOriginalOpens_ReportsTheRest()
    {
        var a = BackupFile("a.tar.gz", BackupFileState.Wrapped);
        var b = BackupFile("b.tar.gz", BackupFileState.Wrapped);
        var c = BackupFile("c.tar.gz", BackupFileState.Wrapped);
        BackupFile("healthy.tar.gz", BackupFileState.Encrypted);
        _rotator.TryRepairWrappedAsync(a, StoredPassphrase, OriginalPassphrase, Arg.Any<CancellationToken>()).Returns(true);
        _rotator.TryRepairWrappedAsync(b, StoredPassphrase, OriginalPassphrase, Arg.Any<CancellationToken>()).Returns(true);
        _rotator.TryRepairWrappedAsync(c, StoredPassphrase, OriginalPassphrase, Arg.Any<CancellationToken>()).Returns(false);

        var result = await _service.RepairWrappedAsync(_directory, OriginalPassphrase, UserId);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.RepairedCount, Is.EqualTo(2));
            Assert.That(result.StillWrappedFiles, Is.EqualTo(new[] { "c.tar.gz" }));
        }
        await _rotator.DidNotReceive().TryRepairWrappedAsync(Path.Combine(_directory, "healthy.tar.gz"),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _auditService.Received(1).LogEventAsync(AuditEventType.BackupFilesRepaired, Arg.Any<Actor>(),
            Arg.Any<Actor?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RepairWrappedAsync_OuterLayerNotOnStoredPassphrase_ReportsFileAsStillWrapped()
    {
        var a = BackupFile("a.tar.gz", BackupFileState.Wrapped);
        _rotator.TryRepairWrappedAsync(a, StoredPassphrase, OriginalPassphrase, Arg.Any<CancellationToken>())
            .ThrowsAsync(new CryptographicException("outer layer"));

        var result = await _service.RepairWrappedAsync(_directory, OriginalPassphrase, UserId);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.RepairedCount, Is.Zero);
            Assert.That(result.StillWrappedFiles, Is.EqualTo(new[] { "a.tar.gz" }));
        }
    }

    [Test]
    public async Task RepairWrappedAsync_UnexpectedFailure_MovesOnToTheNextFile()
    {
        var a = BackupFile("a.tar.gz", BackupFileState.Wrapped);
        var b = BackupFile("b.tar.gz", BackupFileState.Wrapped);
        _rotator.TryRepairWrappedAsync(a, StoredPassphrase, OriginalPassphrase, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("not an archive inside"));
        _rotator.TryRepairWrappedAsync(b, StoredPassphrase, OriginalPassphrase, Arg.Any<CancellationToken>()).Returns(true);

        var result = await _service.RepairWrappedAsync(_directory, OriginalPassphrase, UserId);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.RepairedCount, Is.EqualTo(1));
            Assert.That(result.StillWrappedFiles, Is.EqualTo(new[] { "a.tar.gz" }));
        }
        await _auditService.Received(1).LogEventAsync(AuditEventType.BackupFilesRepaired, Arg.Any<Actor>(),
            Arg.Any<Actor?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RepairWrappedAsync_WaitsForTheBackupFileLock()
    {
        var a = BackupFile("a.tar.gz", BackupFileState.Wrapped);
        _rotator.TryRepairWrappedAsync(a, StoredPassphrase, OriginalPassphrase, Arg.Any<CancellationToken>()).Returns(true);
        var held = await _fileLock.AcquireAsync();

        var run = _service.RepairWrappedAsync(_directory, OriginalPassphrase, UserId);
        await Task.Delay(100);
        Assert.That(run.IsCompleted, Is.False);

        held.Dispose();
        var result = await run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(result.RepairedCount, Is.EqualTo(1));
    }

    [Test]
    public async Task DeleteDamagedAsync_IgnoresNamesThatAreNotBackups()
    {
        // A backup still being written sits next to the others as a temp file
        var temp = BackupFile("backup_x.tar.gz.1a2b3c4d.tmp", BackupFileState.Unreadable);

        var deleted = await _service.DeleteDamagedAsync(_directory, ["backup_x.tar.gz.1a2b3c4d.tmp"], UserId);

        Assert.That(deleted, Is.Zero);
        Assert.That(File.Exists(temp), Is.True);
    }

    [Test]
    public async Task AuditWriteFailure_DoesNotHideACompletedRepair()
    {
        var a = BackupFile("a.tar.gz", BackupFileState.Wrapped);
        _rotator.TryRepairWrappedAsync(a, StoredPassphrase, OriginalPassphrase, Arg.Any<CancellationToken>()).Returns(true);
        _auditService.LogEventAsync(Arg.Any<AuditEventType>(), Arg.Any<Actor>(), Arg.Any<Actor?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("audit table unavailable"));

        var result = await _service.RepairWrappedAsync(_directory, OriginalPassphrase, UserId);

        Assert.That(result.RepairedCount, Is.EqualTo(1), "the repair happened, so the dialog must be told");
    }

    [Test]
    public async Task RepairWrappedAsync_NothingRepaired_WritesNoAudit()
    {
        var a = BackupFile("a.tar.gz", BackupFileState.Wrapped);
        _rotator.TryRepairWrappedAsync(a, StoredPassphrase, OriginalPassphrase, Arg.Any<CancellationToken>()).Returns(false);

        await _service.RepairWrappedAsync(_directory, OriginalPassphrase, UserId);

        await _auditService.DidNotReceive().LogEventAsync(Arg.Any<AuditEventType>(), Arg.Any<Actor>(),
            Arg.Any<Actor?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DeleteDamagedAsync_DeletesOnlyWrappedOrUnreadableNamesInsideTheDirectory()
    {
        var wrapped = BackupFile("w.tar.gz", BackupFileState.Wrapped);
        var unreadable = BackupFile("u.tar.gz", BackupFileState.Unreadable);
        var healthy = BackupFile("ok.tar.gz", BackupFileState.Encrypted);
        var outside = Path.Combine(_parent, "outside.tar.gz");
        File.WriteAllBytes(outside, []);
        _rotator.InspectAsync(outside, Arg.Any<CancellationToken>()).Returns(BackupFileState.Wrapped);

        var deleted = await _service.DeleteDamagedAsync(_directory,
            ["w.tar.gz", "u.tar.gz", "ok.tar.gz", "../outside.tar.gz", "missing.tar.gz"], UserId);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(deleted, Is.EqualTo(2));
            Assert.That(File.Exists(wrapped), Is.False);
            Assert.That(File.Exists(unreadable), Is.False);
            Assert.That(File.Exists(healthy), Is.True, "a healthy backup is never deleted");
            Assert.That(File.Exists(outside), Is.True, "a name outside the backup directory is never deleted");
        }
        await _auditService.Received(1).LogEventAsync(AuditEventType.BackupFilesDeleted, Arg.Any<Actor>(),
            Arg.Any<Actor?>(), Arg.Is<string?>(v => v!.Contains("w.tar.gz") && v.Contains("u.tar.gz")), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DeleteDamagedAsync_NothingDeleted_WritesNoAudit()
    {
        BackupFile("ok.tar.gz", BackupFileState.Encrypted);

        var deleted = await _service.DeleteDamagedAsync(_directory, ["ok.tar.gz"], UserId);

        Assert.That(deleted, Is.Zero);
        await _auditService.DidNotReceive().LogEventAsync(Arg.Any<AuditEventType>(), Arg.Any<Actor>(),
            Arg.Any<Actor?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    private string BackupFile(string name, BackupFileState state)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllBytes(path, []);
        _rotator.InspectAsync(path, Arg.Any<CancellationToken>()).Returns(state);
        return path;
    }
}
