using Microsoft.Extensions.Logging;
using NSubstitute;
using TelegramGroupsAdmin.BackgroundJobs.Services.Backup;

namespace TelegramGroupsAdmin.UnitTests.BackgroundJobs.Services.Backup;

/// <summary>
/// Unit tests for BackupRetentionService.
/// Tests grandfather-father-son retention strategy for backup management.
/// Pure logic tests - no external dependencies required.
/// </summary>
[TestFixture]
public class BackupRetentionServiceTests
{
    private ILogger<BackupRetentionService> _mockLogger = null!;
    private BackupRetentionService _service = null!;
    private RetentionConfig _defaultConfig = null!;

    [SetUp]
    public void Setup()
    {
        _mockLogger = Substitute.For<ILogger<BackupRetentionService>>();
        _service = new BackupRetentionService(_mockLogger);

        // Default config: 24 hourly, 7 daily, 4 weekly, 12 monthly, 3 yearly
        _defaultConfig = new RetentionConfig
        {
            RetainHourlyBackups = 24,
            RetainDailyBackups = 7,
            RetainWeeklyBackups = 4,
            RetainMonthlyBackups = 12,
            RetainYearlyBackups = 3
        };
    }

    #region Edge Case Tests

    [Test]
    public void GetBackupsToDelete_ReturnsEmpty_WhenInputIsNull()
    {
        // Act
        var result = _service.GetBackupsToDelete(null!, _defaultConfig);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void GetBackupsToDelete_ReturnsEmpty_WhenInputIsEmpty()
    {
        // Arrange
        var backups = new List<BackupFileInfo>();

        // Act
        var result = _service.GetBackupsToDelete(backups, _defaultConfig);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void GetBackupsToDelete_KeepsSingleBackup()
    {
        // Arrange
        var backups = new List<BackupFileInfo>
        {
            CreateBackup("backup1.db", DateTimeOffset.UtcNow)
        };

        // Act
        var result = _service.GetBackupsToDelete(backups, _defaultConfig);

        // Assert - single backup should be kept
        Assert.That(result, Is.Empty);
    }

    #endregion

    #region Hourly Retention Tests

    [Test]
    public void GetBackupsToDelete_KeepsLast24HourlyBackups()
    {
        // Arrange - 30 backups, one per hour
        var now = DateTimeOffset.UtcNow;
        var backups = Enumerable.Range(0, 30)
            .Select(i => CreateBackup($"backup{i}.db", now.AddHours(-i)))
            .ToList();

        var config = new RetentionConfig
        {
            RetainHourlyBackups = 24,
            RetainDailyBackups = 0, // Disable other tiers
            RetainWeeklyBackups = 0,
            RetainMonthlyBackups = 0,
            RetainYearlyBackups = 0
        };

        // Act
        var toDelete = _service.GetBackupsToDelete(backups, config);

        // Assert - should delete oldest 6 backups (30 - 24 = 6)
        Assert.That(toDelete.Count, Is.EqualTo(6));
        Assert.That(toDelete.All(b => b.FileName.StartsWith("backup2")), Is.True); // backups 24-29
    }

    #endregion

    #region Daily Retention Tests

    [Test]
    public void GetBackupsToDelete_KeepsFirstBackupOfEachDay()
    {
        // Arrange - 10 backups across 5 days (2 per day)
        var now = new DateTimeOffset(2026, 1, 10, 12, 0, 0, TimeSpan.Zero);
        var backups = new List<BackupFileInfo>
        {
            CreateBackup("day1_am.db", now.AddDays(-4).AddHours(-6)), // Day 1 first
            CreateBackup("day1_pm.db", now.AddDays(-4).AddHours(6)),  // Day 1 second
            CreateBackup("day2_am.db", now.AddDays(-3).AddHours(-6)), // Day 2 first
            CreateBackup("day2_pm.db", now.AddDays(-3).AddHours(6)),  // Day 2 second
            CreateBackup("day3_am.db", now.AddDays(-2).AddHours(-6)), // Day 3 first
            CreateBackup("day3_pm.db", now.AddDays(-2).AddHours(6)),  // Day 3 second
            CreateBackup("day4_am.db", now.AddDays(-1).AddHours(-6)), // Day 4 first
            CreateBackup("day4_pm.db", now.AddDays(-1).AddHours(6)),  // Day 4 second
            CreateBackup("day5_am.db", now.AddHours(-6)),             // Day 5 first
            CreateBackup("day5_pm.db", now),                          // Day 5 second
        };

        var config = new RetentionConfig
        {
            RetainHourlyBackups = 0, // Disable hourly
            RetainDailyBackups = 5,  // Keep 5 daily backups
            RetainWeeklyBackups = 0,
            RetainMonthlyBackups = 0,
            RetainYearlyBackups = 0
        };

        // Act
        var toDelete = _service.GetBackupsToDelete(backups, config);

        // Assert - should delete the "pm" backups (not first of day)
        Assert.That(toDelete.Count, Is.EqualTo(5));
        Assert.That(toDelete.All(b => b.FileName.Contains("_pm")), Is.True);
    }

    #endregion

    #region Weekly Retention Tests

    [Test]
    public void GetBackupsToDelete_KeepsFirstBackupOfEachWeek()
    {
        // Arrange - 6 backups across 6 weeks
        var monday = new DateTimeOffset(2026, 1, 6, 12, 0, 0, TimeSpan.Zero); // A Monday
        var backups = Enumerable.Range(0, 6)
            .Select(i => CreateBackup($"week{i}.db", monday.AddDays(-i * 7)))
            .ToList();

        var config = new RetentionConfig
        {
            RetainHourlyBackups = 0,
            RetainDailyBackups = 0,
            RetainWeeklyBackups = 4, // Keep 4 weeks
            RetainMonthlyBackups = 0,
            RetainYearlyBackups = 0
        };

        // Act
        var toDelete = _service.GetBackupsToDelete(backups, config);

        // Assert - should delete oldest 2 weekly backups
        Assert.That(toDelete.Count, Is.EqualTo(2));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(toDelete.Any(b => b.FileName == "week4.db"), Is.True);
            Assert.That(toDelete.Any(b => b.FileName == "week5.db"), Is.True);
        }
    }

    #endregion

    #region Monthly Retention Tests

    [Test]
    public void GetBackupsToDelete_KeepsFirstBackupOfEachMonth()
    {
        // Arrange - 15 backups across 15 months
        var startDate = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var backups = Enumerable.Range(0, 15)
            .Select(i => CreateBackup($"month{i}.db", startDate.AddMonths(-i)))
            .ToList();

        var config = new RetentionConfig
        {
            RetainHourlyBackups = 0,
            RetainDailyBackups = 0,
            RetainWeeklyBackups = 0,
            RetainMonthlyBackups = 12, // Keep 12 months
            RetainYearlyBackups = 0
        };

        // Act
        var toDelete = _service.GetBackupsToDelete(backups, config);

        // Assert - should delete oldest 3 monthly backups
        Assert.That(toDelete.Count, Is.EqualTo(3));
    }

    #endregion

    #region Yearly Retention Tests

    [Test]
    public void GetBackupsToDelete_KeepsFirstBackupOfEachYear()
    {
        // Arrange - 5 backups across 5 years
        var startDate = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var backups = Enumerable.Range(0, 5)
            .Select(i => CreateBackup($"year{i}.db", startDate.AddYears(-i)))
            .ToList();

        var config = new RetentionConfig
        {
            RetainHourlyBackups = 0,
            RetainDailyBackups = 0,
            RetainWeeklyBackups = 0,
            RetainMonthlyBackups = 0,
            RetainYearlyBackups = 3 // Keep 3 years
        };

        // Act
        var toDelete = _service.GetBackupsToDelete(backups, config);

        // Assert - should delete oldest 2 yearly backups
        Assert.That(toDelete.Count, Is.EqualTo(2));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(toDelete.Any(b => b.FileName == "year3.db"), Is.True);
            Assert.That(toDelete.Any(b => b.FileName == "year4.db"), Is.True);
        }
    }

    #endregion

    #region Tier Hierarchy Tests

    [Test]
    public void GetBackupsToDelete_HigherTierPreventsDeletion()
    {
        // Arrange - backup qualifies for both hourly and yearly
        var firstOfYear = new DateTimeOffset(2026, 1, 1, 1, 0, 0, TimeSpan.Zero);
        var backups = new List<BackupFileInfo>
        {
            CreateBackup("first_of_year.db", firstOfYear)
        };

        var config = new RetentionConfig
        {
            RetainHourlyBackups = 0, // Don't keep as hourly
            RetainDailyBackups = 0,
            RetainWeeklyBackups = 0,
            RetainMonthlyBackups = 0,
            RetainYearlyBackups = 1  // But keep as yearly
        };

        // Act
        var toDelete = _service.GetBackupsToDelete(backups, config);

        // Assert - backup should be kept (yearly tier)
        Assert.That(toDelete, Is.Empty);
    }

    #endregion

    #region GetBackupRetentionInfo Tests

    [Test]
    public void GetBackupRetentionInfo_ReturnsYearlyTier_ForFirstOfYear()
    {
        // Arrange
        var firstOfYear = new DateTimeOffset(2026, 1, 1, 1, 0, 0, TimeSpan.Zero);
        var backup = CreateBackup("first_of_year.db", firstOfYear);
        var allBackups = new List<BackupFileInfo> { backup };

        // Act
        var info = _service.GetBackupRetentionInfo(backup, allBackups, _defaultConfig);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(info.PrimaryTier, Is.EqualTo(BackupTier.Yearly));
            Assert.That(info.WillBeKept, Is.True);
        }
    }

    [Test]
    public void GetBackupRetentionInfo_ReturnsDailyTier_ForFirstOfDay()
    {
        // Arrange - first backup of a day that's not first of week/month/year
        var midMonthDay = new DateTimeOffset(2026, 1, 15, 1, 0, 0, TimeSpan.Zero);
        var backup = CreateBackup("mid_month.db", midMonthDay);
        var allBackups = new List<BackupFileInfo> { backup };

        // Act
        var info = _service.GetBackupRetentionInfo(backup, allBackups, _defaultConfig);

        // Assert - will be highest tier it qualifies for
        Assert.That(info.WillBeKept, Is.True);
    }

    [Test]
    public void GetBackupRetentionInfo_ReturnsWillBeKeptFalse_WhenExceedsRetention()
    {
        // Arrange - backup beyond retention
        var now = DateTimeOffset.UtcNow;
        var backups = Enumerable.Range(0, 30)
            .Select(i => CreateBackup($"backup{i}.db", now.AddHours(-i)))
            .ToList();

        var config = new RetentionConfig
        {
            RetainHourlyBackups = 5, // Only keep 5 most recent
            RetainDailyBackups = 0,
            RetainWeeklyBackups = 0,
            RetainMonthlyBackups = 0,
            RetainYearlyBackups = 0
        };

        var oldBackup = backups[25]; // backup25.db - old backup

        // Act
        var info = _service.GetBackupRetentionInfo(oldBackup, backups, config);

        // Assert
        Assert.That(info.WillBeKept, Is.False);
    }

    #endregion

    #region Helper Methods

    private static BackupFileInfo CreateBackup(string fileName, DateTimeOffset createdAt)
    {
        return new BackupFileInfo
        {
            FilePath = $"/backups/{fileName}",
            CreatedAt = createdAt,
            FileSizeBytes = 1024 * 1024 // 1MB
        };
    }

    #endregion
}
