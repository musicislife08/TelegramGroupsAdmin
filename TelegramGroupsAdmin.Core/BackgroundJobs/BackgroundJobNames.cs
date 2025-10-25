namespace TelegramGroupsAdmin.Core.BackgroundJobs;

/// <summary>
/// Centralized constants for background job names
/// Must match [TickerFunction] attribute names exactly
/// </summary>
public static class BackgroundJobNames
{
    /// <summary>
    /// Scheduled database backups with retention management
    /// TickerFunction: "scheduled_backup"
    /// </summary>
    public const string ScheduledBackup = "scheduled_backup";

    /// <summary>
    /// Message cleanup (deletes expired messages and media files)
    /// Note: This is a BackgroundService, not a TickerQ job (no manual "Run Now")
    /// </summary>
    public const string MessageCleanup = "message_cleanup";

    /// <summary>
    /// User photo refresh (downloads updated profile photos from Telegram)
    /// TickerFunction: "refresh_user_photos"
    /// </summary>
    public const string UserPhotoRefresh = "refresh_user_photos";

    /// <summary>
    /// URL blocklist sync (updates blocklists from upstream sources)
    /// TickerFunction: "BlocklistSync"
    /// </summary>
    public const string BlocklistSync = "BlocklistSync";

    /// <summary>
    /// Database maintenance (VACUUM and ANALYZE operations)
    /// TickerFunction: "database_maintenance"
    /// </summary>
    public const string DatabaseMaintenance = "database_maintenance";

    /// <summary>
    /// Chat health check (monitors bot permissions and admin lists)
    /// TickerFunction: "chat_health_check"
    /// Replaces PeriodicTimer in TelegramAdminBotService
    /// </summary>
    public const string ChatHealthCheck = "chat_health_check";
}

/// <summary>
/// Centralized constants for background job settings keys
/// Prevents magic strings in Settings dictionaries
/// </summary>
public static class BackgroundJobSettings
{
    // Scheduled Backup settings
    public const string RetentionDays = "RetentionDays";
    public const string BackupDirectory = "BackupDirectory";

    // Message Cleanup settings
    public const string RetentionHours = "RetentionHours";

    // User Photo Refresh settings
    public const string DaysBack = "DaysBack";

    // Database Maintenance settings
    public const string RunVacuum = "RunVacuum";
    public const string RunAnalyze = "RunAnalyze";
}
