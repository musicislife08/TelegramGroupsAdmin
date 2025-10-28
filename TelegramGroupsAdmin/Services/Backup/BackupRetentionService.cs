namespace TelegramGroupsAdmin.Services.Backup;

/// <summary>
/// Service for managing backup retention with grandfather-father-son strategy
/// </summary>
public class BackupRetentionService
{
    private readonly ILogger<BackupRetentionService> _logger;

    public BackupRetentionService(ILogger<BackupRetentionService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Determines which backup files should be deleted based on retention policy
    /// Uses grandfather-father-son (5-tier) strategy
    /// </summary>
    /// <param name="backupFiles">All backup files with their metadata</param>
    /// <param name="retentionConfig">Retention configuration</param>
    /// <returns>List of backup files to delete</returns>
    public List<BackupFileInfo> GetBackupsToDelete(
        List<BackupFileInfo> backupFiles,
        RetentionConfig retentionConfig)
    {
        if (backupFiles == null || backupFiles.Count == 0)
            return new List<BackupFileInfo>();

        // Sort by creation time (oldest first)
        var sortedBackups = backupFiles.OrderBy(b => b.CreatedAt).ToList();

        // Classify backups into tiers
        var classified = ClassifyBackups(sortedBackups);

        // Determine which backups to keep in each tier
        var toKeep = new HashSet<string>();

        // Keep hourly backups (most recent N)
        var hourlyToKeep = classified[BackupTier.Hourly]
            .OrderByDescending(b => b.CreatedAt)
            .Take(retentionConfig.RetainHourlyBackups)
            .ToList();
        foreach (var backup in hourlyToKeep)
            toKeep.Add(backup.FilePath);

        // Keep daily backups (most recent N)
        var dailyToKeep = classified[BackupTier.Daily]
            .OrderByDescending(b => b.CreatedAt)
            .Take(retentionConfig.RetainDailyBackups)
            .ToList();
        foreach (var backup in dailyToKeep)
            toKeep.Add(backup.FilePath);

        // Keep weekly backups (most recent N)
        var weeklyToKeep = classified[BackupTier.Weekly]
            .OrderByDescending(b => b.CreatedAt)
            .Take(retentionConfig.RetainWeeklyBackups)
            .ToList();
        foreach (var backup in weeklyToKeep)
            toKeep.Add(backup.FilePath);

        // Keep monthly backups (most recent N)
        var monthlyToKeep = classified[BackupTier.Monthly]
            .OrderByDescending(b => b.CreatedAt)
            .Take(retentionConfig.RetainMonthlyBackups)
            .ToList();
        foreach (var backup in monthlyToKeep)
            toKeep.Add(backup.FilePath);

        // Keep yearly backups (most recent N)
        var yearlyToKeep = classified[BackupTier.Yearly]
            .OrderByDescending(b => b.CreatedAt)
            .Take(retentionConfig.RetainYearlyBackups)
            .ToList();
        foreach (var backup in yearlyToKeep)
            toKeep.Add(backup.FilePath);

        // Backups not in "toKeep" set should be deleted
        var toDelete = sortedBackups.Where(b => !toKeep.Contains(b.FilePath)).ToList();

        _logger.LogInformation(
            "Retention analysis: {TotalBackups} total, keeping {KeepCount} ({Hourly}h/{Daily}d/{Weekly}w/{Monthly}m/{Yearly}y), deleting {DeleteCount}",
            backupFiles.Count,
            toKeep.Count,
            hourlyToKeep.Count,
            dailyToKeep.Count,
            weeklyToKeep.Count,
            monthlyToKeep.Count,
            yearlyToKeep.Count,
            toDelete.Count);

        return toDelete;
    }

    /// <summary>
    /// Classifies backups into retention tiers (hourly/daily/weekly/monthly/yearly)
    /// A backup can qualify for multiple tiers (e.g., first backup of day also qualifies as weekly)
    /// </summary>
    private Dictionary<BackupTier, List<BackupFileInfo>> ClassifyBackups(List<BackupFileInfo> backups)
    {
        var classified = new Dictionary<BackupTier, List<BackupFileInfo>>
        {
            [BackupTier.Hourly] = new List<BackupFileInfo>(),
            [BackupTier.Daily] = new List<BackupFileInfo>(),
            [BackupTier.Weekly] = new List<BackupFileInfo>(),
            [BackupTier.Monthly] = new List<BackupFileInfo>(),
            [BackupTier.Yearly] = new List<BackupFileInfo>()
        };

        // Track "first backup of period" for each tier
        var firstOfDay = new HashSet<string>();
        var firstOfWeek = new HashSet<string>();
        var firstOfMonth = new HashSet<string>();
        var firstOfYear = new HashSet<string>();

        foreach (var backup in backups)
        {
            var date = backup.CreatedAt;

            // All backups qualify as hourly
            classified[BackupTier.Hourly].Add(backup);

            // First backup of each day qualifies as daily
            var dayKey = date.ToString("yyyy-MM-dd");
            if (!firstOfDay.Contains(dayKey))
            {
                classified[BackupTier.Daily].Add(backup);
                firstOfDay.Add(dayKey);
            }

            // First backup of each week qualifies as weekly (week starts Sunday)
            var weekKey = GetWeekKey(date);
            if (!firstOfWeek.Contains(weekKey))
            {
                classified[BackupTier.Weekly].Add(backup);
                firstOfWeek.Add(weekKey);
            }

            // First backup of each month qualifies as monthly
            var monthKey = date.ToString("yyyy-MM");
            if (!firstOfMonth.Contains(monthKey))
            {
                classified[BackupTier.Monthly].Add(backup);
                firstOfMonth.Add(monthKey);
            }

            // First backup of each year qualifies as yearly
            var yearKey = date.ToString("yyyy");
            if (!firstOfYear.Contains(yearKey))
            {
                classified[BackupTier.Yearly].Add(backup);
                firstOfYear.Add(yearKey);
            }
        }

        return classified;
    }

    /// <summary>
    /// Gets week key in format "YYYY-Www" (ISO 8601 week date)
    /// </summary>
    private static string GetWeekKey(DateTimeOffset date)
    {
        // ISO 8601 week starts on Monday
        var startOfWeek = date.AddDays(-(int)date.DayOfWeek + (int)DayOfWeek.Monday);
        if (date.DayOfWeek == DayOfWeek.Sunday)
        {
            // Sunday belongs to previous week in ISO 8601
            startOfWeek = startOfWeek.AddDays(-7);
        }

        // ISO week numbering
        var jan1 = new DateTimeOffset(startOfWeek.Year, 1, 1, 0, 0, 0, date.Offset);
        var daysOffset = (int)jan1.DayOfWeek - 1; // Monday = 0
        if (daysOffset < 0) daysOffset += 7;

        var firstMonday = jan1.AddDays(-daysOffset);
        var weekNumber = ((startOfWeek - firstMonday).Days / 7) + 1;

        return $"{startOfWeek.Year}-W{weekNumber:D2}";
    }
}

/// <summary>
/// Backup tier classification (grandfather-father-son strategy)
/// </summary>
public enum BackupTier
{
    Hourly,
    Daily,
    Weekly,
    Monthly,
    Yearly
}

/// <summary>
/// Information about a backup file for retention analysis
/// </summary>
public class BackupFileInfo
{
    public required string FilePath { get; init; }
    public string FileName => Path.GetFileName(FilePath); // Computed property
    public required DateTimeOffset CreatedAt { get; init; }
    public required long FileSizeBytes { get; init; }
    public bool IsEncrypted { get; init; } // For backup browser
    public BackupTier? HighestTier { get; set; } // Calculated by retention service
}

/// <summary>
/// Retention policy configuration (5-tier)
/// </summary>
public class RetentionConfig
{
    /// <summary>
    /// Number of hourly backups to retain (default: 24 = last 24 hours)
    /// </summary>
    public int RetainHourlyBackups { get; set; } = 24;

    /// <summary>
    /// Number of daily backups to retain (default: 7 = last 7 days)
    /// </summary>
    public int RetainDailyBackups { get; set; } = 7;

    /// <summary>
    /// Number of weekly backups to retain (default: 4 = last 4 weeks)
    /// </summary>
    public int RetainWeeklyBackups { get; set; } = 4;

    /// <summary>
    /// Number of monthly backups to retain (default: 12 = last 12 months)
    /// </summary>
    public int RetainMonthlyBackups { get; set; } = 12;

    /// <summary>
    /// Number of yearly backups to retain (default: 3 = last 3 years)
    /// </summary>
    public int RetainYearlyBackups { get; set; } = 3;
}
