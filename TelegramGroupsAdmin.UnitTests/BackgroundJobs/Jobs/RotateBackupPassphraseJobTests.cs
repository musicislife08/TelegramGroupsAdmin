using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Quartz;
using TelegramGroupsAdmin.BackgroundJobs.Jobs;
using TelegramGroupsAdmin.BackgroundJobs.Metrics;
using TelegramGroupsAdmin.BackgroundJobs.Services.Backup;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Core.JobPayloads;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Data.Services;

namespace TelegramGroupsAdmin.UnitTests.BackgroundJobs.Jobs;

/// <summary>
/// Unit tests for <see cref="RotateBackupPassphraseJob"/>: which files it rotates, which it skips,
/// and when it is allowed to replace the stored passphrase. The archive work itself is covered by
/// BackupArchiveRotatorTests; here the rotator is a substitute and the directory holds empty files.
/// </summary>
[TestFixture]
public class RotateBackupPassphraseJobTests
{
    private const string OldPassphrase = "old-passphrase-12345";
    private const string NewPassphrase = "new-passphrase-67890";
    private const string ProtectedNewPassphrase = "protected-new-passphrase";

    private IBackupArchiveRotator _rotator = null!;
    private IPassphraseManagementService _passphraseService = null!;
    private IDataProtectionService _dataProtection = null!;
    private IAuditService _auditService = null!;
    private BackupFileLock _fileLock = null!;
    private RotateBackupPassphraseJob _job = null!;
    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _rotator = Substitute.For<IBackupArchiveRotator>();
        _passphraseService = Substitute.For<IPassphraseManagementService>();
        _passphraseService.GetDecryptedPassphraseAsync().Returns(OldPassphrase);
        _dataProtection = Substitute.For<IDataProtectionService>();
        _dataProtection.Unprotect(ProtectedNewPassphrase).Returns(NewPassphrase);
        _auditService = Substitute.For<IAuditService>();

        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IAuditService)).Returns(_auditService);
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(serviceProvider);
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);

        _fileLock = new BackupFileLock();
        _job = new RotateBackupPassphraseJob(
            _rotator, _passphraseService, _dataProtection, _fileLock, scopeFactory,
            NullLogger<RotateBackupPassphraseJob>.Instance, new JobMetrics());
        _directory = Directory.CreateTempSubdirectory("tga_rotation_job_").FullName;
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, recursive: true);

    [Test]
    public async Task AllRotatable_UpdatesStoredPassphrase()
    {
        var a = BackupFile("a.tar.gz", BackupFileState.Encrypted);
        var b = BackupFile("b.tar.gz", BackupFileState.Encrypted);
        _rotator.ReencryptAsync(Arg.Any<string>(), OldPassphrase, NewPassphrase, Arg.Any<CancellationToken>())
            .Returns(ReencryptOutcome.Reencrypted);

        await _job.Execute(Context(Payload()));

        await _rotator.Received(1).ReencryptAsync(a, OldPassphrase, NewPassphrase, Arg.Any<CancellationToken>());
        await _rotator.Received(1).ReencryptAsync(b, OldPassphrase, NewPassphrase, Arg.Any<CancellationToken>());
        await _passphraseService.Received(1).UpdateEncryptionConfigAsync(NewPassphrase);
    }

    [Test]
    public async Task PlainBackup_IsRotatedToo()
    {
        var plain = BackupFile("plain.tar.gz", BackupFileState.Plain);

        await _job.Execute(Context(Payload()));

        await _rotator.Received(1).ReencryptAsync(plain, OldPassphrase, NewPassphrase, Arg.Any<CancellationToken>());
        await _passphraseService.Received(1).UpdateEncryptionConfigAsync(NewPassphrase);
    }

    [Test]
    public async Task WrappedFile_IsSkipped_AndDoesNotBlockTheUpdate()
    {
        var healthy = BackupFile("healthy.tar.gz", BackupFileState.Encrypted);
        var wrapped = BackupFile("wrapped.tar.gz", BackupFileState.Wrapped);

        await _job.Execute(Context(Payload()));

        await _rotator.Received(1).ReencryptAsync(healthy, OldPassphrase, NewPassphrase, Arg.Any<CancellationToken>());
        await _rotator.DidNotReceive().ReencryptAsync(wrapped, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _passphraseService.Received(1).UpdateEncryptionConfigAsync(NewPassphrase);
        await _auditService.Received(1).LogEventAsync(
            AuditEventType.BackupPassphraseRotated, Arg.Any<Actor>(), Arg.Any<Actor?>(),
            Arg.Is<string?>(v => v!.Contains("skipped 1")), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Audit_IsWrittenAfterTheRotation_WithWhatHappened()
    {
        var path = BackupFile("a.tar.gz", BackupFileState.Encrypted);
        _rotator.ReencryptAsync(path, OldPassphrase, NewPassphrase, Arg.Any<CancellationToken>())
            .Returns(ReencryptOutcome.Reencrypted);

        await _job.Execute(Context(Payload()));

        Received.InOrder(() =>
        {
            _rotator.ReencryptAsync(path, OldPassphrase, NewPassphrase, Arg.Any<CancellationToken>());
            _passphraseService.UpdateEncryptionConfigAsync(NewPassphrase);
            _auditService.LogEventAsync(AuditEventType.BackupPassphraseRotated, Arg.Any<Actor>(), Arg.Any<Actor?>(),
                Arg.Is<string?>(v => v!.Contains("Re-encrypted 1")), Arg.Any<CancellationToken>());
        });
    }

    [Test]
    public async Task AuditWriteFailure_DoesNotFailACompletedRotation()
    {
        BackupFile("a.tar.gz", BackupFileState.Encrypted);
        _auditService.LogEventAsync(Arg.Any<AuditEventType>(), Arg.Any<Actor>(), Arg.Any<Actor?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("audit table unavailable"));

        Assert.That(async () => await _job.Execute(Context(Payload())), Throws.Nothing,
            "the backups and the stored passphrase already changed; failing now would trigger a retry");
        await _passphraseService.Received(1).UpdateEncryptionConfigAsync(NewPassphrase);
    }

    [Test]
    public async Task UnreadableFile_ChangesNoFiles()
    {
        BackupFile("a-healthy.tar.gz", BackupFileState.Encrypted);
        BackupFile("b-wrapped.tar.gz", BackupFileState.Wrapped);
        BackupFile("c-junk.tar.gz", BackupFileState.Unreadable);

        Assert.That(async () => await _job.Execute(Context(Payload())), Throws.InvalidOperationException);
        await _rotator.DidNotReceive().ReencryptAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _rotator.DidNotReceive().RewrapAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task WrappedFile_OuterLayerMovesToTheNewPassphrase()
    {
        var wrapped = BackupFile("wrapped.tar.gz", BackupFileState.Wrapped);

        await _job.Execute(Context(Payload()));

        await _rotator.Received(1).RewrapAsync(wrapped, OldPassphrase, NewPassphrase, Arg.Any<CancellationToken>());
        await _passphraseService.Received(1).UpdateEncryptionConfigAsync(NewPassphrase);
    }

    [Test]
    public async Task WrappedFile_WhoseOuterLayerDoesNotOpen_IsSkipped_NotFailed()
    {
        var wrapped = BackupFile("wrapped.tar.gz", BackupFileState.Wrapped);
        _rotator.RewrapAsync(wrapped, OldPassphrase, NewPassphrase, Arg.Any<CancellationToken>())
            .ThrowsAsync(new CryptographicException("outer layer"));

        await _job.Execute(Context(Payload()));

        await _passphraseService.Received(1).UpdateEncryptionConfigAsync(NewPassphrase);
    }

    [Test]
    public async Task Rotation_WaitsForTheBackupFileLock()
    {
        BackupFile("a.tar.gz", BackupFileState.Encrypted);
        var held = await _fileLock.AcquireAsync();

        var run = _job.Execute(Context(Payload()));
        await Task.Delay(100);
        Assert.That(run.IsCompleted, Is.False, "a backup is being written, so the rotation must wait");
        await _passphraseService.DidNotReceive().UpdateEncryptionConfigAsync(Arg.Any<string>());

        held.Dispose();
        await run.WaitAsync(TimeSpan.FromSeconds(5));
        await _passphraseService.Received(1).UpdateEncryptionConfigAsync(NewPassphrase);
    }

    [Test]
    public async Task UnreadableFile_KeepsOldPassphrase_AndThrows()
    {
        BackupFile("healthy.tar.gz", BackupFileState.Encrypted);
        BackupFile("junk.tar.gz", BackupFileState.Unreadable);

        Assert.That(async () => await _job.Execute(Context(Payload())), Throws.InvalidOperationException);
        await _passphraseService.DidNotReceive().UpdateEncryptionConfigAsync(Arg.Any<string>());
    }

    [Test]
    public async Task ReencryptFailure_KeepsOldPassphrase_AndThrows()
    {
        var path = BackupFile("a.tar.gz", BackupFileState.Encrypted);
        _rotator.ReencryptAsync(path, OldPassphrase, NewPassphrase, Arg.Any<CancellationToken>())
            .ThrowsAsync(new CryptographicException("wrong passphrase"));

        Assert.That(async () => await _job.Execute(Context(Payload())), Throws.InvalidOperationException);
        await _passphraseService.DidNotReceive().UpdateEncryptionConfigAsync(Arg.Any<string>());
    }

    [Test]
    public async Task AlreadyCurrentFiles_CountAsSuccess()
    {
        var path = BackupFile("a.tar.gz", BackupFileState.Encrypted);
        _rotator.ReencryptAsync(path, OldPassphrase, NewPassphrase, Arg.Any<CancellationToken>())
            .Returns(ReencryptOutcome.AlreadyCurrent);

        await _job.Execute(Context(Payload()));

        await _passphraseService.Received(1).UpdateEncryptionConfigAsync(NewPassphrase);
    }

    [Test]
    public async Task PayloadIsUnprotectedBeforeUse()
    {
        var path = BackupFile("a.tar.gz", BackupFileState.Encrypted);

        await _job.Execute(Context(Payload()));

        _dataProtection.Received(1).Unprotect(ProtectedNewPassphrase);
        await _rotator.Received(1).ReencryptAsync(path, OldPassphrase, NewPassphrase, Arg.Any<CancellationToken>());
        await _rotator.DidNotReceive().ReencryptAsync(Arg.Any<string>(), Arg.Any<string>(), ProtectedNewPassphrase, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PayloadWithoutProtectedPassphrase_ExitsWithoutChanges()
    {
        BackupFile("a.tar.gz", BackupFileState.Encrypted);
        // The payload shape queued by versions before this fix.
        var legacyJson = JsonSerializer.Serialize(new { NewPassphrase = NewPassphrase, BackupDirectory = _directory, UserId = "user-id" });

        await _job.Execute(Context(legacyJson));

        await _rotator.DidNotReceive().ReencryptAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _passphraseService.DidNotReceive().UpdateEncryptionConfigAsync(Arg.Any<string>());
    }

    private string BackupFile(string name, BackupFileState state)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllBytes(path, []);
        _rotator.InspectAsync(path, Arg.Any<CancellationToken>()).Returns(state);
        return path;
    }

    private string Payload() =>
        JsonSerializer.Serialize(new RotateBackupPassphrasePayload(ProtectedNewPassphrase, _directory, "user-id"));

    private static IJobExecutionContext Context(string payloadJson)
    {
        var context = Substitute.For<IJobExecutionContext>();
        context.MergedJobDataMap.Returns(new JobDataMap { { JobDataKeys.PayloadJson, payloadJson } });
        context.CancellationToken.Returns(CancellationToken.None);
        return context;
    }
}
