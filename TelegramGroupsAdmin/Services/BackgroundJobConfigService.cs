using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data;

namespace TelegramGroupsAdmin.Services;

/// <summary>
/// Service for managing background job configurations
/// Stores configuration in configs.background_jobs_config JSONB column
/// </summary>
public class BackgroundJobConfigService : IBackgroundJobConfigService
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<BackgroundJobConfigService> _logger;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
        WriteIndented = false
    };

    public BackgroundJobConfigService(
        IDbContextFactory<AppDbContext> contextFactory,
        ILogger<BackgroundJobConfigService> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task<BackgroundJobConfig?> GetJobConfigAsync(string jobName, CancellationToken cancellationToken = default)
    {
        var allJobs = await GetAllJobsAsync(cancellationToken);
        return allJobs.GetValueOrDefault(jobName);
    }

    public async Task<Dictionary<string, BackgroundJobConfig>> GetAllJobsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Get global config (chat_id = NULL)
        var config = await context.Configs
            .AsNoTracking()
            .Where(c => c.ChatId == null)
            .FirstOrDefaultAsync(cancellationToken);

        if (config?.BackgroundJobsConfig == null)
        {
            _logger.LogDebug("No background jobs config found, returning empty dictionary");
            return new Dictionary<string, BackgroundJobConfig>();
        }

        try
        {
            var jobsConfig = JsonSerializer.Deserialize<BackgroundJobsConfig>(config.BackgroundJobsConfig, _jsonOptions);
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
            .Where(c => c.ChatId == null)
            .FirstOrDefaultAsync(cancellationToken);

        if (configRecord == null)
        {
            configRecord = new Data.Models.ConfigRecordDto
            {
                ChatId = null,
                CreatedAt = DateTimeOffset.UtcNow
            };
            context.Configs.Add(configRecord);
        }

        // Parse existing jobs config or create new
        var jobsConfig = string.IsNullOrEmpty(configRecord.BackgroundJobsConfig)
            ? new BackgroundJobsConfig()
            : JsonSerializer.Deserialize<BackgroundJobsConfig>(configRecord.BackgroundJobsConfig, _jsonOptions) ?? new BackgroundJobsConfig();

        // Check if schedule changed (interval or cron) - if so, clear NextRunAt to reschedule immediately
        if (jobsConfig.Jobs.TryGetValue(jobName, out var existingJob))
        {
            var scheduleChanged = existingJob.ScheduleType != config.ScheduleType ||
                                  existingJob.IntervalDuration != config.IntervalDuration ||
                                  existingJob.CronExpression != config.CronExpression;

            if (scheduleChanged)
            {
                config.NextRunAt = null; // Clear scheduled time - will be recalculated on next scheduler run
                _logger.LogInformation("Schedule changed for {JobName}, clearing NextRunAt for immediate reschedule", jobName);
            }
        }

        // Update the specific job
        jobsConfig.Jobs[jobName] = config;

        // Serialize back to JSON
        configRecord.BackgroundJobsConfig = JsonSerializer.Serialize(jobsConfig);
        configRecord.UpdatedAt = DateTimeOffset.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Updated background job config for {JobName}", jobName);
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

    public async Task EnsureDefaultConfigsAsync(CancellationToken cancellationToken = default)
    {
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
                if (existingConfig.ScheduleType == "interval" && string.IsNullOrEmpty(existingConfig.IntervalDuration))
                {
                    _logger.LogWarning("Repairing invalid IntervalDuration for {JobName} (was null, setting to {Default})",
                        jobName, defaultConfig.IntervalDuration);
                    existingConfig.IntervalDuration = defaultConfig.IntervalDuration;
                    needsRepair = true;
                }
                else if (existingConfig.ScheduleType == "cron" && string.IsNullOrEmpty(existingConfig.CronExpression))
                {
                    _logger.LogWarning("Repairing invalid CronExpression for {JobName} (was null, setting to {Default})",
                        jobName, defaultConfig.CronExpression);
                    existingConfig.CronExpression = defaultConfig.CronExpression;
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
                Enabled = false, // Disabled by default
                ScheduleType = "cron",
                CronExpression = "0 2 * * *", // Daily at 2 AM
                Settings = new Dictionary<string, object>
                {
                    [BackgroundJobSettings.RetainHourlyBackups] = 24,  // Last 24 hours
                    [BackgroundJobSettings.RetainDailyBackups] = 7,    // Last 7 days
                    [BackgroundJobSettings.RetainWeeklyBackups] = 4,   // Last 4 weeks
                    [BackgroundJobSettings.RetainMonthlyBackups] = 12, // Last 12 months
                    [BackgroundJobSettings.RetainYearlyBackups] = 3,   // Last 3 years
                    [BackgroundJobSettings.BackupDirectory] = "/data/backups"
                }
            },
            [BackgroundJobNames.MessageCleanup] = new BackgroundJobConfig
            {
                JobName = BackgroundJobNames.MessageCleanup,
                DisplayName = "Message Cleanup",
                Description = "Delete old messages and their media files based on retention policy",
                Enabled = true, // Already exists, just adding config
                ScheduleType = "interval",
                IntervalDuration = "24h", // Every 24 hours
                Settings = new Dictionary<string, object>
                {
                    [BackgroundJobSettings.RetentionHours] = 720 // 30 days default
                }
            },
            [BackgroundJobNames.UserPhotoRefresh] = new BackgroundJobConfig
            {
                JobName = BackgroundJobNames.UserPhotoRefresh,
                DisplayName = "User Photo Refresh",
                Description = "Refresh user profile photos from Telegram",
                Enabled = true,
                ScheduleType = "cron",
                CronExpression = "0 3 * * *", // Daily at 3 AM
                Settings = new Dictionary<string, object>
                {
                    [BackgroundJobSettings.DaysBack] = 7 // Refresh photos for users active in last 7 days
                }
            },
            [BackgroundJobNames.BlocklistSync] = new BackgroundJobConfig
            {
                JobName = BackgroundJobNames.BlocklistSync,
                DisplayName = "URL Blocklist Sync",
                Description = "Sync URL blocklists from upstream sources",
                Enabled = true,
                ScheduleType = "interval",
                IntervalDuration = "7d", // Weekly (7 days)
                Settings = new Dictionary<string, object>()
            },
            [BackgroundJobNames.DatabaseMaintenance] = new BackgroundJobConfig
            {
                JobName = BackgroundJobNames.DatabaseMaintenance,
                DisplayName = "Database Maintenance",
                Description = "Run VACUUM and ANALYZE on PostgreSQL database",
                Enabled = false, // Stub for future, disabled by default
                ScheduleType = "cron",
                CronExpression = "0 4 * * 0", // Weekly on Sunday at 4 AM
                Settings = new Dictionary<string, object>
                {
                    [BackgroundJobSettings.RunVacuum] = true,
                    [BackgroundJobSettings.RunAnalyze] = true
                }
            },
            [BackgroundJobNames.ChatHealthCheck] = new BackgroundJobConfig
            {
                JobName = BackgroundJobNames.ChatHealthCheck,
                DisplayName = "Chat Health Monitoring",
                Description = "Monitor chat health, bot permissions, and admin lists",
                Enabled = true,
                ScheduleType = "interval",
                IntervalDuration = "30m", // Every 30 minutes
                Settings = new Dictionary<string, object>()
            }
        };
    }
}
