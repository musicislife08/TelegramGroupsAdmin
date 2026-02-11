using TelegramGroupsAdmin.BackgroundJobs.Services.Backup;

namespace TelegramGroupsAdmin.UnitTests.BackgroundJobs.Services.Backup;

[TestFixture]
public class BackupFileInfoTests
{
    [Test]
    public void ParseTimestampFromFilename_ValidFilename_ReturnsCorrectTimestamp()
    {
        var result = BackupFileInfo.ParseTimestampFromFilename("/data/backups/backup_2026-01-27_14-30-45.tar.gz");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Year, Is.EqualTo(2026));
            Assert.That(result.Month, Is.EqualTo(1));
            Assert.That(result.Day, Is.EqualTo(27));
            Assert.That(result.Hour, Is.EqualTo(14));
            Assert.That(result.Minute, Is.EqualTo(30));
            Assert.That(result.Second, Is.EqualTo(45));
            Assert.That(result.Offset, Is.EqualTo(TimeSpan.Zero));
        }
    }

    [Test]
    public void ParseTimestampFromFilename_FilenameOnly_ReturnsCorrectTimestamp()
    {
        // Uses a temp file so the fallback path doesn't crash on missing file
        var tempFile = Path.GetTempFileName();
        var renamedPath = Path.Combine(Path.GetDirectoryName(tempFile)!, "backup_2025-12-31_23-59-59.tar.gz");
        File.Move(tempFile, renamedPath);

        try
        {
            var result = BackupFileInfo.ParseTimestampFromFilename(renamedPath);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.Year, Is.EqualTo(2025));
                Assert.That(result.Month, Is.EqualTo(12));
                Assert.That(result.Day, Is.EqualTo(31));
                Assert.That(result.Hour, Is.EqualTo(23));
                Assert.That(result.Minute, Is.EqualTo(59));
                Assert.That(result.Second, Is.EqualTo(59));
            }
        }
        finally
        {
            File.Delete(renamedPath);
        }
    }

    [Test]
    public void ParseTimestampFromFilename_LeapYearDate_ParsesCorrectly()
    {
        var result = BackupFileInfo.ParseTimestampFromFilename("/data/backups/backup_2024-02-29_12-00-00.tar.gz");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Year, Is.EqualTo(2024));
            Assert.That(result.Month, Is.EqualTo(2));
            Assert.That(result.Day, Is.EqualTo(29));
        }
    }

    [Test]
    public void ParseTimestampFromFilename_InvalidFilename_FallsBackToFileTime()
    {
        // Create a real temp file with a non-matching name so the fallback reads modification time
        var tempFile = Path.GetTempFileName();
        var renamedPath = Path.Combine(Path.GetDirectoryName(tempFile)!, "not-a-backup.tar.gz");
        File.Move(tempFile, renamedPath);

        try
        {
            var result = BackupFileInfo.ParseTimestampFromFilename(renamedPath);

            // Should return the file's last write time (just created, so recent)
            Assert.That(result, Is.EqualTo(new DateTimeOffset(File.GetLastWriteTimeUtc(renamedPath), TimeSpan.Zero))
                .Within(TimeSpan.FromSeconds(2)));
        }
        finally
        {
            File.Delete(renamedPath);
        }
    }

    [Test]
    public void ParseTimestampFromFilename_ShortFilename_FallsBackToFileTime()
    {
        var tempFile = Path.GetTempFileName();
        var renamedPath = Path.Combine(Path.GetDirectoryName(tempFile)!, "short.tar.gz");
        File.Move(tempFile, renamedPath);

        try
        {
            var result = BackupFileInfo.ParseTimestampFromFilename(renamedPath);

            Assert.That(result, Is.EqualTo(new DateTimeOffset(File.GetLastWriteTimeUtc(renamedPath), TimeSpan.Zero))
                .Within(TimeSpan.FromSeconds(2)));
        }
        finally
        {
            File.Delete(renamedPath);
        }
    }
}
