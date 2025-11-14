namespace TelegramGroupsAdmin.Core.BackgroundJobs;

/// <summary>
/// Centralized constants for background job names
/// Must match Quartz.NET job class names exactly
/// </summary>
public static class BackgroundJobNames
{
    /// <summary>
    /// Scheduled database backups with retention management
    /// Quartz Job: ScheduledBackupJob
    /// </summary>
    public const string ScheduledBackup = "ScheduledBackupJob";

    /// <summary>
    /// Message cleanup (deletes old messages based on retention policy)
    /// Background Service: CleanupBackgroundService (always running)
    /// </summary>
    public const string MessageCleanup = "MessageCleanup";

    /// <summary>
    /// User photo refresh (downloads updated profile photos from Telegram)
    /// Quartz Job: RefreshUserPhotosJob
    /// </summary>
    public const string UserPhotoRefresh = "RefreshUserPhotosJob";

    /// <summary>
    /// URL blocklist sync (updates blocklists from upstream sources)
    /// Quartz Job: BlocklistSyncJob
    /// </summary>
    public const string BlocklistSync = "BlocklistSyncJob";

    /// <summary>
    /// Database maintenance (VACUUM and ANALYZE operations)
    /// Quartz Job: DatabaseMaintenanceJob
    /// </summary>
    public const string DatabaseMaintenance = "DatabaseMaintenanceJob";

    /// <summary>
    /// Chat health check (monitors bot permissions and admin lists)
    /// Quartz Job: ChatHealthCheckJob
    /// </summary>
    public const string ChatHealthCheck = "ChatHealthCheckJob";
}

/// <summary>
/// Centralized constants for background job settings keys
/// Prevents magic strings in Settings dictionaries
/// </summary>
public static class BackgroundJobSettings
{
    // Scheduled Backup settings (granular 5-tier retention)
    public const string RetainHourlyBackups = "RetainHourlyBackups";
    public const string RetainDailyBackups = "RetainDailyBackups";
    public const string RetainWeeklyBackups = "RetainWeeklyBackups";
    public const string RetainMonthlyBackups = "RetainMonthlyBackups";
    public const string RetainYearlyBackups = "RetainYearlyBackups";
    public const string BackupDirectory = "BackupDirectory";

    // Message Cleanup settings
    public const string RetentionHours = "RetentionHours";

    // User Photo Refresh settings
    public const string DaysBack = "DaysBack";

    // Database Maintenance settings
    public const string RunVacuum = "RunVacuum";
    public const string RunAnalyze = "RunAnalyze";
}
