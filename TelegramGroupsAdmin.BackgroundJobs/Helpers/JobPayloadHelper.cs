using System.Text.Json;
using Microsoft.Extensions.Logging;
using Quartz;
using TelegramGroupsAdmin.Core.BackgroundJobs;

namespace TelegramGroupsAdmin.BackgroundJobs.Helpers;

/// <summary>
/// Helper methods for extracting and deserializing job payloads from Quartz job context.
/// Provides consistent, defensive payload handling across all on-demand jobs.
/// </summary>
public static class JobPayloadHelper
{
    /// <summary>
    /// Extracts and deserializes a typed payload from the job context.
    /// Returns null if the payload is missing (e.g., job triggered via stale cron schedule).
    /// Automatically cleans up stale triggers to prevent repeated failures.
    /// </summary>
    /// <typeparam name="T">The payload type to deserialize to</typeparam>
    /// <param name="context">The Quartz job execution context</param>
    /// <param name="logger">Logger for warning messages</param>
    /// <param name="jobName">Job name for logging (defaults to job type name)</param>
    /// <returns>The deserialized payload, or null if not present</returns>
    public static async Task<T?> TryGetPayloadAsync<T>(
        IJobExecutionContext context,
        ILogger logger,
        string? jobName = null) where T : class
    {
        jobName ??= typeof(T).Name.Replace("Payload", "Job");

        // Check if payload exists in merged job data map (includes trigger data)
        if (!context.MergedJobDataMap.ContainsKey(JobDataKeys.PayloadJson))
        {
            logger.LogWarning(
                "{JobName} triggered without payload - stale trigger detected. " +
                "This job should only be triggered via TriggerNowAsync. " +
                "Removing stale trigger and skipping execution. TriggerKey: {TriggerKey}",
                jobName,
                context.Trigger.Key);

            await CleanupStaleTriggerAsync(context, logger, jobName);
            return null;
        }

        var payloadJson = context.MergedJobDataMap.GetString(JobDataKeys.PayloadJson);
        if (string.IsNullOrEmpty(payloadJson))
        {
            logger.LogWarning(
                "{JobName} has empty payload JSON - stale trigger detected. " +
                "Removing stale trigger and skipping execution. TriggerKey: {TriggerKey}",
                jobName,
                context.Trigger.Key);

            await CleanupStaleTriggerAsync(context, logger, jobName);
            return null;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<T>(payloadJson);
            if (payload == null)
            {
                logger.LogError(
                    "{JobName} failed to deserialize payload (returned null). PayloadJson: {PayloadJson}",
                    jobName,
                    payloadJson);
            }
            return payload;
        }
        catch (JsonException ex)
        {
            logger.LogError(
                ex,
                "{JobName} failed to deserialize payload. PayloadJson: {PayloadJson}",
                jobName,
                payloadJson);
            return null;
        }
    }

    /// <summary>
    /// Removes a stale trigger from the Quartz scheduler.
    /// Called when an on-demand job is triggered without the required payload.
    /// </summary>
    private static async Task CleanupStaleTriggerAsync(
        IJobExecutionContext context,
        ILogger logger,
        string jobName)
    {
        try
        {
            var triggerKey = context.Trigger.Key;
            var removed = await context.Scheduler.UnscheduleJob(triggerKey);

            if (removed)
            {
                logger.LogWarning(
                    "Successfully removed stale trigger {TriggerKey} for {JobName}. " +
                    "This trigger was likely created by an outdated configuration or database restore.",
                    triggerKey,
                    jobName);
            }
            else
            {
                logger.LogWarning(
                    "Trigger {TriggerKey} for {JobName} was already removed or is a one-time trigger.",
                    triggerKey,
                    jobName);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to remove stale trigger {TriggerKey} for {JobName}. Manual cleanup may be required.",
                context.Trigger.Key,
                jobName);
        }
    }

    /// <summary>
    /// Extracts and deserializes a typed payload from the job context.
    /// Throws if the payload is missing or cannot be deserialized.
    /// Use this for jobs where missing payload indicates a bug (not stale data).
    /// </summary>
    /// <typeparam name="T">The payload type to deserialize to</typeparam>
    /// <param name="context">The Quartz job execution context</param>
    /// <param name="jobName">Job name for error messages</param>
    /// <returns>The deserialized payload</returns>
    /// <exception cref="InvalidOperationException">Thrown if payload is missing or invalid</exception>
    public static T GetRequiredPayload<T>(IJobExecutionContext context, string? jobName = null) where T : class
    {
        jobName ??= typeof(T).Name.Replace("Payload", "Job");

        var payloadJson = context.MergedJobDataMap.GetString(JobDataKeys.PayloadJson)
            ?? throw new InvalidOperationException($"{jobName}: payload not found in job data");

        return JsonSerializer.Deserialize<T>(payloadJson)
            ?? throw new InvalidOperationException($"{jobName}: failed to deserialize payload");
    }
}
