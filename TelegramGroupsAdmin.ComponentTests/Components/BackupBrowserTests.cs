using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;
using TelegramGroupsAdmin.BackgroundJobs.Services.Backup;
using TelegramGroupsAdmin.Components.Shared;

namespace TelegramGroupsAdmin.ComponentTests.Components;

/// <summary>
/// Component tests for BackupBrowser.razor. The browser lists the .tar.gz files in a real temp
/// directory; the archive inspection is substituted per file.
/// </summary>
[TestFixture]
public class BackupBrowserTests : BunitContext
{
    private const string HealthyName = "backup_2026-05-01_030000.tar.gz";
    private const string DamagedName = "backup_2026-06-01_030000.tar.gz";

    private readonly IBackupService _backupService = Substitute.For<IBackupService>();
    private readonly IBackupArchiveRotator _rotator = Substitute.For<IBackupArchiveRotator>();
    private string _directory = null!;

    // bUnit forbids registering services after the first render, and NUnit reuses this fixture
    // instance for every test, so services are registered once here. Each test uses its own temp
    // directory, so per-path substitute setups never collide.
    public BackupBrowserTests()
    {
        Services.AddSingleton(_backupService);
        Services.AddSingleton(_rotator);
        Services.AddSingleton<IBackupRetentionService>(new BackupRetentionService(NullLogger<BackupRetentionService>.Instance));
        Services.AddMudServices(options =>
        {
            options.PopoverOptions.ThrowOnDuplicateProvider = false;
            options.PopoverOptions.CheckForPopoverProvider = false;
        });
        JSInterop.Mode = JSRuntimeMode.Loose;
        this.AddTestWebUser();
    }

    [SetUp]
    public void SetUp() => _directory = Directory.CreateTempSubdirectory("tga_browser_").FullName;

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, recursive: true);

    [Test]
    public void DamagedFile_DoesNotHideTheReadableBackups()
    {
        BackupFile(HealthyName, BackupFileState.Encrypted);
        BackupFile(DamagedName, BackupFileState.Wrapped);

        var cut = Render<BackupBrowser>(p => p.Add(x => x.BackupDirectory, _directory));

        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain(HealthyName), "the readable backup is still listed");
            Assert.That(cut.Markup, Does.Not.Contain("No backups found"));
        });
    }

    [Test]
    public void DamagedFile_IsReportedWithWhereToFixIt()
    {
        BackupFile(HealthyName, BackupFileState.Encrypted);
        BackupFile(DamagedName, BackupFileState.Wrapped);

        var cut = Render<BackupBrowser>(p => p.Add(x => x.BackupDirectory, _directory));

        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("1 file in this folder can't be read as a backup"));
            Assert.That(cut.Markup, Does.Contain("Rotate Encryption Passphrase"));
        });
    }

    [Test]
    public void OnlyReadableBackups_ShowNoDamageWarning()
    {
        BackupFile(HealthyName, BackupFileState.Encrypted);

        var cut = Render<BackupBrowser>(p => p.Add(x => x.BackupDirectory, _directory));

        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain(HealthyName));
            Assert.That(cut.Markup, Does.Not.Contain("can't be read as a backup"));
        });
    }

    private string BackupFile(string name, BackupFileState state)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllBytes(path, [0x1f, 0x8b]);
        _rotator.InspectAsync(path, Arg.Any<CancellationToken>()).Returns(state);
        return path;
    }
}
