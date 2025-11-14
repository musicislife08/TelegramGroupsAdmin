namespace TelegramGroupsAdmin.Core.BackgroundJobs;

/// <summary>
/// Constants for Quartz.NET JobDataMap keys - ensures compile-time safety and prevents typos
/// </summary>
public static class JobDataKeys
{
    /// <summary>
    /// Key for serialized JSON payload (used by QuartzJobScheduler for ad-hoc jobs)
    /// </summary>
    public const string PayloadJson = "PayloadJson";

    /// <summary>
    /// Key for payload type's AssemblyQualifiedName (used for deserialization)
    /// </summary>
    public const string PayloadType = "PayloadType";

    /// <summary>
    /// Key for retry count (used by RetryJobListener to track retry attempts)
    /// </summary>
    public const string RetryCount = "RetryCount";

    /// <summary>
    /// Key for original exception message (stored by RetryJobListener for debugging)
    /// </summary>
    public const string LastException = "LastException";
}
