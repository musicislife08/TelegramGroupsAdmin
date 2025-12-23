namespace TelegramGroupsAdmin.BackgroundJobs.Constants;

/// <summary>
/// Constants for Quartz.NET configuration.
/// </summary>
public static class QuartzConstants
{
    /// <summary>
    /// Maximum number of concurrent jobs in the Quartz.NET thread pool.
    /// </summary>
    public const int MaxConcurrency = 4;
}
