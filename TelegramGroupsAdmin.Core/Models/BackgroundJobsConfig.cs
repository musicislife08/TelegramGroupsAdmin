namespace TelegramGroupsAdmin.Core.Models;

/// <summary>
/// Container for all background job configurations
/// This is what gets serialized to the JSONB column
/// </summary>
public class BackgroundJobsConfig
{
    /// <summary>
    /// Dictionary of job configurations keyed by job name
    /// </summary>
    public Dictionary<string, BackgroundJobConfig> Jobs { get; set; } = new();
}
