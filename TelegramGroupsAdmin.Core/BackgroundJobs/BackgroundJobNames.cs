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
    /// Data cleanup (deletes expired messages, reports, callbacks, notifications based on retention)
    /// Quartz Job: DataCleanupJob
    /// </summary>
    public const string DataCleanup = "DataCleanup";

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

    /// <summary>
    /// Classifier retraining (retrains ML.NET SDCA text classifier and Naive Bayes classifier with latest data)
    /// Quartz Job: ClassifierRetrainingJob
    /// </summary>
    public const string ClassifierRetraining = "ClassifierRetrainingJob";

    // ============================================
    // Ad-Hoc Jobs (one-time delayed execution)
    // ============================================

    /// <summary>
    /// Delete a single message (auto-delete fallback messages)
    /// Quartz Job: DeleteMessageJob
    /// </summary>
    public const string DeleteMessage = "DeleteMessage";

    /// <summary>
    /// Delete all messages from a user across chats (spambot cleanup)
    /// Quartz Job: DeleteUserMessagesJob
    /// </summary>
    public const string DeleteUserMessages = "DeleteUserMessages";

    /// <summary>
    /// Fetch user profile photo from Telegram (on-demand caching)
    /// Quartz Job: FetchUserPhotoJob
    /// </summary>
    public const string FetchUserPhoto = "FetchUserPhoto";

    /// <summary>
    /// Scan file for malware (ClamAV + VirusTotal)
    /// Quartz Job: FileScanJob
    /// </summary>
    public const string FileScan = "FileScan";

    /// <summary>
    /// Rotate backup encryption passphrase (scheduled key rotation)
    /// Quartz Job: RotateBackupPassphraseJob
    /// </summary>
    public const string RotateBackupPassphrase = "RotateBackupPassphrase";

    /// <summary>
    /// Expire temporary ban (automatic unban after duration)
    /// Quartz Job: TempbanExpiryJob
    /// </summary>
    public const string TempbanExpiry = "TempbanExpiry";

    /// <summary>
    /// Welcome message timeout (kick user if they don't accept rules)
    /// Quartz Job: WelcomeTimeoutJob
    /// </summary>
    public const string WelcomeTimeout = "WelcomeTimeout";

    /// <summary>
    /// On-demand profile scan triggered by profile diff detection or manual re-scan.
    /// Quartz Job: ProfileScanJob
    /// </summary>
    public const string ProfileScan = "ProfileScan";

    // ============================================
    // Scheduled Jobs (cron-based, UI-configurable)
    // ============================================

    /// <summary>
    /// Periodic re-scan of user profiles to detect changes (bio, channel, stories).
    /// Quartz Job: ProfileRescanJob
    /// </summary>
    public const string ProfileRescan = "ProfileRescanJob";
}
