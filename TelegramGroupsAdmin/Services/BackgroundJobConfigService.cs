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
    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

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
            // Only create if doesn't exist
            if (!existing.ContainsKey(jobName))
            {
                await UpdateJobConfigAsync(jobName, defaultConfig, cancellationToken);
                _logger.LogInformation("Created default config for {JobName}", jobName);
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
                    [BackgroundJobSettings.RetentionDays] = 7
                }
            },
            [BackgroundJobNames.MessageCleanup] = new BackgroundJobConfig
            {
                JobName = BackgroundJobNames.MessageCleanup,
                DisplayName = "Message Cleanup",
                Description = "Delete old messages and their media files based on retention policy",
                Enabled = true, // Already exists, just adding config
                ScheduleType = "interval",
                IntervalHours = 24, // Every 24 hours
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
                IntervalHours = 168, // Weekly (7 days)
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
            }
        };
    }
}
