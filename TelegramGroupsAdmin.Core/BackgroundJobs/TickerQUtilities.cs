using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TickerQ.Utilities.Models.Ticker;
using TickerQ.Utilities.Interfaces.Managers;

namespace TelegramGroupsAdmin.Core.BackgroundJobs;

/// <summary>
/// Helper methods for scheduling TickerQ background jobs
/// Shared across all domain libraries to avoid coupling
/// </summary>
public static class TickerQUtilities
{
    /// <summary>
    /// Schedule a TickerQ job with standardized error handling and logging (non-generic overload)
    /// </summary>
    /// <param name="serviceProvider">Service provider for creating scopes</param>
    /// <param name="logger">Logger instance for tracking job scheduling</param>
    /// <param name="functionName">Name of the TickerQ function to execute</param>
    /// <param name="payload">Job payload data (will be serialized as its runtime type)</param>
    /// <param name="delaySeconds">Delay in seconds before job execution (0 for immediate)</param>
    /// <param name="retries">Number of retries on failure (default: 0)</param>
    /// <param name="retryIntervals">Retry intervals in seconds (default: null)</param>
    /// <returns>Job ID if successful, null if failed</returns>
    public static Task<Guid?> ScheduleJobAsync(
        IServiceProvider serviceProvider,
        ILogger logger,
        string functionName,
        object payload,
        int delaySeconds,
        int retries = 0,
        int[]? retryIntervals = null)
    {
        // Use reflection to call the generic version with the runtime type
        var payloadType = payload.GetType();

        // Find the generic method
        var methods = typeof(TickerQUtilities).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        var genericMethod = methods
            .Where(m => m.Name == nameof(ScheduleJobAsync))
            .Where(m => m.IsGenericMethod)
            .Where(m => m.GetParameters().Length == 7)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("Could not find generic ScheduleJobAsync method");

        // Make it concrete with the runtime payload type
        var concreteMethod = genericMethod.MakeGenericMethod(payloadType);

        // Invoke it
        var task = concreteMethod.Invoke(null, [serviceProvider, logger, functionName, payload, delaySeconds, retries, retryIntervals
        ]);
        return (Task<Guid?>)(task ?? Task.FromResult<Guid?>(null));
    }

    /// <summary>
    /// Schedule a TickerQ job with standardized error handling and logging (generic version)
    /// </summary>
    /// <typeparam name="TPayload">The job payload type</typeparam>
    /// <param name="serviceProvider">Service provider for creating scopes</param>
    /// <param name="logger">Logger instance for tracking job scheduling</param>
    /// <param name="functionName">Name of the TickerQ function to execute</param>
    /// <param name="payload">Job payload data</param>
    /// <param name="delaySeconds">Delay in seconds before job execution (0 for immediate)</param>
    /// <param name="retries">Number of retries on failure (default: 0)</param>
    /// <param name="retryIntervals">Retry intervals in seconds (default: null)</param>
    /// <returns>Job ID if successful, null if failed</returns>
    public static async Task<Guid?> ScheduleJobAsync<TPayload>(
        IServiceProvider serviceProvider,
        ILogger logger,
        string functionName,
        TPayload payload,
        int delaySeconds,
        int retries = 0,
        int[]? retryIntervals = null)
    {
        try
        {
            logger.LogDebug(
                "Scheduling TickerQ job - Function: {FunctionName}, Delay: {DelaySeconds}s, Retries: {Retries}",
                functionName,
                delaySeconds,
                retries);

            // Get TickerQ manager from scope (it's scoped)
            using var scope = serviceProvider.CreateScope();
            var timeTickerManager = scope.ServiceProvider.GetRequiredService<ITimeTickerManager<TimeTicker>>();

            var executionTime = DateTimeOffset.UtcNow.AddSeconds(delaySeconds);
            var request = global::TickerQ.Utilities.TickerHelper.CreateTickerRequest(payload);

            var result = await timeTickerManager.AddAsync(new TimeTicker
            {
                Function = functionName,
                ExecutionTime = executionTime.UtcDateTime, // Use UtcDateTime to preserve timezone
                Request = request,
                Retries = retries,
                RetryIntervals = retryIntervals
            });

            // Note: TickerQ 2.5.3 has typo "IsSucceded" instead of "IsSucceeded"
            if (!result.IsSucceded)
            {
                logger.LogWarning(
                    "Failed to schedule TickerQ job {FunctionName}: {Error}",
                    functionName,
                    result.Exception?.Message ?? "Unknown error");
                return null;
            }

            logger.LogDebug(
                "Successfully scheduled TickerQ job {FunctionName} (JobId: {JobId}, ExecutionTime: {ExecutionTime})",
                functionName,
                result.Result?.Id,
                executionTime);

            return result.Result?.Id;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error scheduling TickerQ job {FunctionName}",
                functionName);
            return null;
        }
    }

    /// <summary>
    /// Cancel a TickerQ job by ID
    /// </summary>
    /// <param name="serviceProvider">Service provider for creating scopes</param>
    /// <param name="logger">Logger instance for tracking job cancellation</param>
    /// <param name="jobId">Job ID to cancel</param>
    /// <returns>True if successful, false if failed</returns>
    public static async Task<bool> CancelJobAsync(
        IServiceProvider serviceProvider,
        ILogger logger,
        Guid jobId)
    {
        try
        {
            logger.LogDebug("Attempting to cancel TickerQ job {JobId}", jobId);

            using var scope = serviceProvider.CreateScope();
            var timeTickerManager = scope.ServiceProvider.GetRequiredService<ITimeTickerManager<TimeTicker>>();

            await timeTickerManager.DeleteAsync(jobId);

            logger.LogInformation("Successfully cancelled TickerQ job {JobId}", jobId);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to cancel TickerQ job {JobId}", jobId);
            return false;
        }
    }
}
