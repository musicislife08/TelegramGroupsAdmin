using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using HumanCron.Quartz.Abstractions;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Core.Extensions;
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

    public async Task UpdateJobConfigAsync(string jobName, BackgroundJobConfig config, WebUserIdentity? changedBy = null, CancellationToken cancellationToken = default)
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
                _logger.LogInformation("Schedule changed for {JobName} by {User}, clearing NextRunAt for immediate reschedule",
                    jobName, changedBy?.ToLogInfo() ?? "system");
                requiresResync = true;
            }

            if (enabledChanged)
            {
                _logger.LogInformation("Enabled status changed for {JobName} from {Old} to {New} by {User}",
                    jobName, existingJob.Enabled, config.Enabled, changedBy?.ToLogInfo() ?? "system");
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

        _logger.LogDebug("Updated background job config for {JobName}", jobName);

        // Only trigger re-sync if Schedule or Enabled changed (not for timestamp updates)
        if (requiresResync)
        {
            _logger.LogDebug("Triggering Quartz re-sync for {JobName} due to config change", jobName);
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

        await UpdateJobConfigAsync(jobName, config, cancellationToken: cancellationToken);
    }

    public async Task EnsureDefaultConfigsAsync(CancellationToken cancellationToken = default)
    {
        var existing = await GetAllJobsAsync(cancellationToken);

        // Define default job configurations
        var defaults = GetDefaultJobConfigs();

        foreach (var (jobName, defaultConfig) in defaults)
        {
            if (!existing.TryGetValue(jobName, out var existingConfig))
            {
                // Create new config if doesn't exist
                await UpdateJobConfigAsync(jobName, defaultConfig, cancellationToken: cancellationToken);
                _logger.LogInformation("Created default config for {JobName}", jobName);
            }
            else
            {
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
                    await UpdateJobConfigAsync(jobName, existingConfig, cancellationToken: cancellationToken);
                    _logger.LogInformation("Repaired config for {JobName}", jobName);
                }
            }
        }

        // Remove unknown jobs that aren't in defaults (e.g., renamed/deleted jobs)
        await RemoveUnknownJobsAsync(existing, defaults, cancellationToken);
    }

    /// <summary>
    /// Removes job configs that don't correspond to any known job in BackgroundJobNames.
    /// This handles cleanup after jobs are renamed or removed.
    /// </summary>
    private async Task RemoveUnknownJobsAsync(
        Dictionary<string, BackgroundJobConfig> existing,
        Dictionary<string, BackgroundJobConfig> defaults,
        CancellationToken cancellationToken)
    {
        var unknownJobs = existing.Keys.Except(defaults.Keys).ToList();

        if (unknownJobs.Count == 0)
            return;

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var configRecord = await context.Configs
            .Where(c => c.ChatId == 0)
            .FirstOrDefaultAsync(cancellationToken);

        if (configRecord?.BackgroundJobsConfig == null)
            return;

        var jobsConfig = JsonSerializer.Deserialize<BackgroundJobsConfig>(configRecord.BackgroundJobsConfig, JsonOptions);
        if (jobsConfig == null)
            return;

        foreach (var jobName in unknownJobs)
        {
            jobsConfig.Jobs.Remove(jobName);
            _logger.LogInformation("Removed unknown job config {JobName} (job no longer exists)", jobName);
        }

        configRecord.BackgroundJobsConfig = JsonSerializer.Serialize(jobsConfig, JsonOptions);
        await context.SaveChangesAsync(cancellationToken);
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
            [BackgroundJobNames.DataCleanup] = new BackgroundJobConfig
            {
                JobName = BackgroundJobNames.DataCleanup,
                DisplayName = "Data Cleanup",
                Description = "Delete expired messages, reports, callback contexts, and notifications based on retention policies",
                Enabled = false,
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
            },
            [BackgroundJobNames.ProfileRescan] = new BackgroundJobConfig
            {
                JobName = BackgroundJobNames.ProfileRescan,
                DisplayName = "Profile Re-Scan",
                Description = "Periodically re-scan user profiles via User API to detect changes in bio, personal channel, and pinned stories",
                Enabled = false,
                Schedule = "every 6 hours",
                ProfileRescan = new()
            }
        };
    }
}
