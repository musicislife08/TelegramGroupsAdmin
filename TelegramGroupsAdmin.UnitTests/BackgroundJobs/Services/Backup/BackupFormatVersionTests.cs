using TelegramGroupsAdmin.BackgroundJobs.Services.Backup;

namespace TelegramGroupsAdmin.UnitTests.BackgroundJobs.Services.Backup;

/// <summary>The backup-migration gates compare format versions numerically, not as strings.</summary>
[TestFixture]
public class BackupFormatVersionTests
{
    [TestCase("3.2", "3.10", true)]
    [TestCase("3.10", "3.2", false)]
    [TestCase("3.1", "3.2", true)]
    [TestCase("3.2", "3.2", false)]
    [TestCase("2.0", "2.1", true)]
    [TestCase("3.0", "2.1", false)]
    public void IsOlderThan_ComparesNumerically(string backupVersion, string gate, bool expected)
    {
        Assert.That(BackupFormatVersion.IsOlderThan(backupVersion, gate), Is.EqualTo(expected));
    }

    [TestCase("")]
    [TestCase("three")]
    public void IsOlderThan_UnreadableVersion_RefusesTheBackup(string backupVersion)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => BackupFormatVersion.IsOlderThan(backupVersion, "3.2"));
        Assert.That(ex!.Message, Does.Contain("backup format version"));
    }
}
