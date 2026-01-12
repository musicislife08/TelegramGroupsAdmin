using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using HumanCron.Quartz.Abstractions;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Models.BackgroundJobSettings;
using TelegramGroupsAdmin.Data;

namespace TelegramGroupsAdmin.BackgroundJobs.Services;

/// <summary>
/// Service for managing background job configurations
/// Stores configuration in configs.background_jobs_config JSONB column
/// </summary>
public class BackgroundJobConfigService : IBackgroundJobConfigService
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<BackgroundJobConfigService> _logger;
    private readonly IQuartzScheduleConverter _scheduleConverter;
    private QuartzSchedulingSyncService? _syncService; // Injected lazily to avoid circular dependency

    /// <summary>
    /// Event fired when a job's NextRunAt is updated (for UI refresh via SignalR)
    /// </summary>
    public event Action<string>? JobNextRunAtUpdated;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
        WriteIndented = false
    };

    public BackgroundJobConfigService(
        IDbContextFactory<AppDbContext> contextFactory,
        ILogger<BackgroundJobConfigService> logger,
        IQuartzScheduleConverter scheduleConverter)
    {
        _contextFactory = contextFactory;
        _logger = logger;
        _scheduleConverter = scheduleConverter;
    }

    /// <summary>
    /// Set the sync service reference (called by QuartzSchedulingSyncService after it starts)
    /// Lazy injection to avoid circular dependency during DI registration
    /// </summary>
    public void SetSyncService(QuartzSchedulingSyncService syncService)
    {
        _syncService = syncService;
    }

    public async Task<BackgroundJobConfig?> GetJobConfigAsync(string jobName, CancellationToken cancellationToken = default)
    {
        var allJobs = await GetAllJobsAsync(cancellationToken);
        return allJobs.GetValueOrDefault(jobName);
    }

    public async Task<Dictionary<string, BackgroundJobConfig>> GetAllJobsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Get global config (chat_id = 0)
        var config = await context.Configs
            .AsNoTracking()
            .Where(c => c.ChatId == 0)
            .FirstOrDefaultAsync(cancellationToken);

        if (config?.BackgroundJobsConfig == null)
        {
            _logger.LogDebug("No background jobs config found, returning empty dictionary");
            return new Dictionary<string, BackgroundJobConfig>();
        }

        try
        {
            var jobsConfig = JsonSerializer.Deserialize<BackgroundJobsConfig>(config.BackgroundJobsConfig, JsonOptions);
            return jobsConfig?.Jobs ?? new Dictionary<string, BackgroundJobConfig>();
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize background jobs config");
            return new Dictionary<string, BackgroundJobConfig>();
        }
    }

    public async Task UpdateJobConfigAsync(string jobName, BackgroundJobConfig config, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Get or create global config
        var configRecord = await context.Configs
            .Where(c => c.ChatId == 0)
            .FirstOrDefaultAsync(cancellationToken);

        if (configRecord == null)
        {
            configRecord = new Data.Models.ConfigRecordDto
            {
                ChatId = 0,
                CreatedAt = DateTimeOffset.UtcNow
            };
            context.Configs.Add(configRecord);
        }

        // Parse existing jobs config or create new
        var jobsConfig = string.IsNullOrEmpty(configRecord.BackgroundJobsConfig)
            ? new BackgroundJobsConfig()
            : JsonSerializer.Deserialize<BackgroundJobsConfig>(configRecord.BackgroundJobsConfig, JsonOptions) ?? new BackgroundJobsConfig();

        // Track if meaningful fields changed (Schedule or Enabled) - these require re-sync
        var requiresResync = false;
        var nextRunAtChanged = false;

        // Check if schedule or enabled status changed
        if (jobsConfig.Jobs.TryGetValue(jobName, out var existingJob))
        {
            var scheduleChanged = existingJob.Schedule != config.Schedule;
            var enabledChanged = existingJob.Enabled != config.Enabled;
            nextRunAtChanged = existingJob.NextRunAt != config.NextRunAt;

            if (scheduleChanged)
            {
                config.NextRunAt = null; // Clear scheduled time - will be recalculated on next scheduler run
                _logger.LogInformation("Schedule changed for {JobName}, clearing NextRunAt for immediate reschedule", jobName);
                requiresResync = true;
            }

            if (enabledChanged)
            {
                _logger.LogInformation("Enabled status changed for {JobName} from {Old} to {New}",
                    jobName, existingJob.Enabled, config.Enabled);
                requiresResync = true;
            }
        }
        else
        {
            // New job - requires initial sync
            requiresResync = true;
            nextRunAtChanged = config.NextRunAt.HasValue;
        }

        // Update the specific job
        jobsConfig.Jobs[jobName] = config;

        // Serialize back to JSON
        configRecord.BackgroundJobsConfig = JsonSerializer.Serialize(jobsConfig);
        configRecord.UpdatedAt = DateTimeOffset.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Updated background job config for {JobName}", jobName);

        // Only trigger re-sync if Schedule or Enabled changed (not for timestamp updates)
        if (requiresResync)
        {
            _logger.LogInformation("Triggering Quartz re-sync for {JobName} due to config change", jobName);
            _syncService?.TriggerResync();
        }

        // Fire event for UI refresh if NextRunAt changed (Blazor Server SignalR push)
        if (nextRunAtChanged)
        {
            JobNextRunAtUpdated?.Invoke(jobName);
        }
    }

    public async Task<bool> IsJobEnabledAsync(string jobName, CancellationToken cancellationToken = default)
    {
        var config = await GetJobConfigAsync(jobName, cancellationToken);
        return config?.Enabled ?? false;
    }

    public async Task UpdateJobStatusAsync(
        string jobName,
        DateTimeOffset lastRunAt,
        DateTimeOffset? nextRunAt,
        string? error = null,
        CancellationToken cancellationToken = default)
    {
        var config = await GetJobConfigAsync(jobName, cancellationToken);
        if (config == null)
        {
            _logger.LogWarning("Cannot update status for non-existent job {JobName}", jobName);
            return;
        }

        config.LastRunAt = lastRunAt;
        config.NextRunAt = nextRunAt;
        config.LastError = error;

        await UpdateJobConfigAsync(jobName, config, cancellationToken);
    }

    /// <summary>
    /// Migrates old Settings dictionary format to new typed properties.
    /// This handles upgrades from the pre-typed settings format where all job settings
    /// were stored in a Dictionary&lt;string, object&gt; called "Settings".
    /// </summary>
    private async Task MigrateOldSettingsFormatAsync(CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var configRecord = await context.Configs
            .Where(c => c.ChatId == 0)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrEmpty(configRecord?.BackgroundJobsConfig))
            return;

        try
        {
            using var doc = JsonDocument.Parse(configRecord.BackgroundJobsConfig);

            if (!doc.RootElement.TryGetProperty("Jobs", out var jobsElement))
                return;

            var needsMigration = false;

            // Check if any job has old "Settings" property but no typed settings
            foreach (var jobProp in jobsElement.EnumerateObject())
            {
                var job = jobProp.Value;
                if (job.TryGetProperty("Settings", out _))
                {
                    // Has old format - check if missing new typed properties
                    var hasTypedSettings = job.TryGetProperty("DataCleanup", out _) ||
                                          job.TryGetProperty("ScheduledBackup", out _) ||
                                          job.TryGetProperty("DatabaseMaintenance", out _) ||
                                          job.TryGetProperty("UserPhotoRefresh", out _);

                    if (!hasTypedSettings)
                    {
                        needsMigration = true;
                        break;
                    }
                }
            }

            if (!needsMigration)
                return;

            _logger.LogInformation("Migrating background job settings from old Dictionary format to typed properties");

            // Deserialize with a temporary class that has both old and new format
            var oldConfig = JsonSerializer.Deserialize<OldBackgroundJobsConfig>(configRecord.BackgroundJobsConfig, JsonOptions);
            if (oldConfig?.Jobs == null)
                return;

            var migratedCount = 0;
            foreach (var (jobName, job) in oldConfig.Jobs)
            {
                if (job.Settings == null || job.Settings.Count == 0)
                    continue;

                // Migrate based on job type
                switch (jobName)
                {
                    case BackgroundJobNames.MessageCleanup when job.DataCleanup == null:
                        job.DataCleanup = new DataCleanupSettings
                        {
                            MessageRetention = GetSettingString(job.Settings, "MessageRetention", "30d") ?? "30d",
                            ReportRetention = GetSettingString(job.Settings, "ReportRetention", "30d") ?? "30d",
                            CallbackContextRetention = GetSettingString(job.Settings, "CallbackContextRetention", "7d") ?? "7d",
                            WebNotificationRetention = GetSettingString(job.Settings, "WebNotificationRetention", "7d") ?? "7d"
                        };
                        job.Settings = null;
                        migratedCount++;
                        break;

                    case BackgroundJobNames.ScheduledBackup when job.ScheduledBackup == null:
                        job.ScheduledBackup = new ScheduledBackupSettings
                        {
                            RetainHourlyBackups = GetSettingInt(job.Settings, "RetainHourlyBackups", 24),
                            RetainDailyBackups = GetSettingInt(job.Settings, "RetainDailyBackups", 7),
                            RetainWeeklyBackups = GetSettingInt(job.Settings, "RetainWeeklyBackups", 4),
                            RetainMonthlyBackups = GetSettingInt(job.Settings, "RetainMonthlyBackups", 12),
                            RetainYearlyBackups = GetSettingInt(job.Settings, "RetainYearlyBackups", 3),
                            BackupDirectory = GetSettingString(job.Settings, "BackupDirectory", null)
                        };
                        job.Settings = null;
                        migratedCount++;
                        break;

                    case BackgroundJobNames.DatabaseMaintenance when job.DatabaseMaintenance == null:
                        job.DatabaseMaintenance = new DatabaseMaintenanceSettings
                        {
                            RunVacuum = GetSettingBool(job.Settings, "RunVacuum", true),
                            RunAnalyze = GetSettingBool(job.Settings, "RunAnalyze", true)
                        };
                        job.Settings = null;
                        migratedCount++;
                        break;

                    case BackgroundJobNames.UserPhotoRefresh when job.UserPhotoRefresh == null:
                        job.UserPhotoRefresh = new UserPhotoRefreshSettings
                        {
                            DaysBack = GetSettingInt(job.Settings, "DaysBack", 7)
                        };
                        job.Settings = null;
                        migratedCount++;
                        break;
                }
            }

            if (migratedCount > 0)
            {
                // Save migrated config (serialize without the Settings property since it's null)
                var newJobsConfig = new BackgroundJobsConfig
                {
                    Jobs = oldConfig.Jobs.ToDictionary(
                        kvp => kvp.Key,
                        kvp => new BackgroundJobConfig
                        {
                            JobName = kvp.Value.JobName,
                            DisplayName = kvp.Value.DisplayName,
                            Description = kvp.Value.Description,
                            Enabled = kvp.Value.Enabled,
                            Schedule = kvp.Value.Schedule,
                            LastRunAt = kvp.Value.LastRunAt,
                            NextRunAt = kvp.Value.NextRunAt,
                            LastError = kvp.Value.LastError,
                            DataCleanup = kvp.Value.DataCleanup,
                            ScheduledBackup = kvp.Value.ScheduledBackup,
                            DatabaseMaintenance = kvp.Value.DatabaseMaintenance,
                            UserPhotoRefresh = kvp.Value.UserPhotoRefresh
                        })
                };

                configRecord.BackgroundJobsConfig = JsonSerializer.Serialize(newJobsConfig, JsonOptions);
                configRecord.UpdatedAt = DateTimeOffset.UtcNow;
                await context.SaveChangesAsync(cancellationToken);

                _logger.LogInformation("Successfully migrated {Count} job(s) from old Settings format to typed properties", migratedCount);
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse background jobs config for migration check - will use defaults");
        }
    }

    private static string? GetSettingString(Dictionary<string, object> settings, string key, string? defaultValue)
    {
        if (!settings.TryGetValue(key, out var value))
            return defaultValue;

        return value switch
        {
            JsonElement je => je.GetString() ?? defaultValue,
            string s => s,
            _ => value?.ToString() ?? defaultValue
        };
    }

    private static int GetSettingInt(Dictionary<string, object> settings, string key, int defaultValue)
    {
        if (!settings.TryGetValue(key, out var value))
            return defaultValue;

        return value switch
        {
            JsonElement je when je.ValueKind == JsonValueKind.Number => je.GetInt32(),
            int i => i,
            _ => int.TryParse(value?.ToString(), out var parsed) ? parsed : defaultValue
        };
    }

    private static bool GetSettingBool(Dictionary<string, object> settings, string key, bool defaultValue)
    {
        if (!settings.TryGetValue(key, out var value))
            return defaultValue;

        return value switch
        {
            JsonElement je when je.ValueKind == JsonValueKind.True => true,
            JsonElement je when je.ValueKind == JsonValueKind.False => false,
            bool b => b,
            _ => bool.TryParse(value?.ToString(), out var parsed) ? parsed : defaultValue
        };
    }

    /// <summary>
    /// Temporary class for deserializing old format that had Settings dictionary.
    /// Used only during migration.
    /// </summary>
    private class OldBackgroundJobsConfig
    {
        public Dictionary<string, OldBackgroundJobConfig> Jobs { get; set; } = new();
    }

    private class OldBackgroundJobConfig
    {
        public required string JobName { get; set; }
        public required string DisplayName { get; set; }
        public required string Description { get; set; }
        public bool Enabled { get; set; }
        public required string Schedule { get; set; }
        public DateTimeOffset? LastRunAt { get; set; }
        public DateTimeOffset? NextRunAt { get; set; }
        public string? LastError { get; set; }
        public Dictionary<string, object>? Settings { get; set; }
        // New typed properties (may be populated if partially migrated)
        public DataCleanupSettings? DataCleanup { get; set; }
        public ScheduledBackupSettings? ScheduledBackup { get; set; }
        public DatabaseMaintenanceSettings? DatabaseMaintenance { get; set; }
        public UserPhotoRefreshSettings? UserPhotoRefresh { get; set; }
    }

    public async Task EnsureDefaultConfigsAsync(CancellationToken cancellationToken = default)
    {
        // Migrate old Settings dictionary format to new typed properties
        await MigrateOldSettingsFormatAsync(cancellationToken);

        var existing = await GetAllJobsAsync(cancellationToken);

        // Define default job configurations
        var defaults = GetDefaultJobConfigs();

        foreach (var (jobName, defaultConfig) in defaults)
        {
            if (!existing.ContainsKey(jobName))
            {
                // Create new config if doesn't exist
                await UpdateJobConfigAsync(jobName, defaultConfig, cancellationToken);
                _logger.LogInformation("Created default config for {JobName}", jobName);
            }
            else
            {
                // Validate and repair existing config
                var existingConfig = existing[jobName];
                var needsRepair = false;

                // Check for invalid schedule configuration
                if (string.IsNullOrEmpty(existingConfig.Schedule))
                {
                    _logger.LogWarning("Repairing invalid Schedule for {JobName} (was null, setting to {Default})",
                        jobName, defaultConfig.Schedule);
                    existingConfig.Schedule = defaultConfig.Schedule;
                    needsRepair = true;
                }

                // Update DisplayName and Description from defaults (allows renaming jobs)
                if (existingConfig.DisplayName != defaultConfig.DisplayName)
                {
                    _logger.LogInformation("Updating DisplayName for {JobName}: {Old} → {New}",
                        jobName, existingConfig.DisplayName, defaultConfig.DisplayName);
                    existingConfig.DisplayName = defaultConfig.DisplayName;
                    needsRepair = true;
                }

                if (existingConfig.Description != defaultConfig.Description)
                {
                    existingConfig.Description = defaultConfig.Description;
                    needsRepair = true;
                }

                // Save repaired config
                if (needsRepair)
                {
                    await UpdateJobConfigAsync(jobName, existingConfig, cancellationToken);
                    _logger.LogInformation("Repaired config for {JobName}", jobName);
                }
            }
        }
    }

    private static Dictionary<string, BackgroundJobConfig> GetDefaultJobConfigs()
    {
        return new Dictionary<string, BackgroundJobConfig>
        {
            [BackgroundJobNames.ScheduledBackup] = new BackgroundJobConfig
            {
                JobName = BackgroundJobNames.ScheduledBackup,
                DisplayName = "Scheduled Backups",
                Description = "Automatically backup database on a schedule",
                Enabled = false,
                Schedule = "every day at 2am",
                ScheduledBackup = new ScheduledBackupSettings
                {
                    BackupDirectory = "/data/backups"
                }
            },
            [BackgroundJobNames.MessageCleanup] = new BackgroundJobConfig
            {
                JobName = BackgroundJobNames.MessageCleanup,
                DisplayName = "Data Cleanup",
                Description = "Delete expired messages, reports, callback contexts, and notifications based on retention policies",
                Enabled = true,
                Schedule = "every day",
                DataCleanup = new DataCleanupSettings()
            },
            [BackgroundJobNames.UserPhotoRefresh] = new BackgroundJobConfig
            {
                JobName = BackgroundJobNames.UserPhotoRefresh,
                DisplayName = "User Photo Refresh",
                Description = "Refresh user profile photos from Telegram",
                Enabled = true,
                Schedule = "every day at 3am",
                UserPhotoRefresh = new UserPhotoRefreshSettings()
            },
            [BackgroundJobNames.BlocklistSync] = new BackgroundJobConfig
            {
                JobName = BackgroundJobNames.BlocklistSync,
                DisplayName = "URL Blocklist Sync",
                Description = "Sync URL blocklists from upstream sources",
                Enabled = true,
                Schedule = "every week on sunday at 3am"
            },
            [BackgroundJobNames.DatabaseMaintenance] = new BackgroundJobConfig
            {
                JobName = BackgroundJobNames.DatabaseMaintenance,
                DisplayName = "Database Maintenance",
                Description = "Run VACUUM and ANALYZE on PostgreSQL database",
                Enabled = false,
                Schedule = "every week on sunday at 4am",
                DatabaseMaintenance = new DatabaseMaintenanceSettings()
            },
            [BackgroundJobNames.ChatHealthCheck] = new BackgroundJobConfig
            {
                JobName = BackgroundJobNames.ChatHealthCheck,
                DisplayName = "Chat Health Monitoring",
                Description = "Monitor chat health, bot permissions, and admin lists",
                Enabled = true,
                Schedule = "every 30 minutes"
            },
            [BackgroundJobNames.TextClassifierRetraining] = new BackgroundJobConfig
            {
                JobName = BackgroundJobNames.TextClassifierRetraining,
                DisplayName = "ML Text Classifier Retraining",
                Description = "Retrain ML.NET SDCA spam classifier with latest training data",
                Enabled = true,
                Schedule = "every 8 hours"
            }
        };
    }
}
