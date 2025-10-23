using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Services;

/// <summary>
/// Service for managing background job configurations
/// Reads/writes from configs.background_jobs_config JSONB column
/// </summary>
public interface IBackgroundJobConfigService
{
    /// <summary>
    /// Gets configuration for a specific job
    /// </summary>
    Task<BackgroundJobConfig?> GetJobConfigAsync(string jobName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all job configurations
    /// </summary>
    Task<Dictionary<string, BackgroundJobConfig>> GetAllJobsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates configuration for a specific job
    /// </summary>
    Task UpdateJobConfigAsync(string jobName, BackgroundJobConfig config, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a job is enabled
    /// </summary>
    Task<bool> IsJobEnabledAsync(string jobName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the last run time and next run time for a job
    /// </summary>
    Task UpdateJobStatusAsync(string jobName, DateTimeOffset lastRunAt, DateTimeOffset? nextRunAt, string? error = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Initializes default job configurations if they don't exist
    /// </summary>
    Task EnsureDefaultConfigsAsync(CancellationToken cancellationToken = default);
}
