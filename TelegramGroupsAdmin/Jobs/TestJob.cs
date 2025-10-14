using Microsoft.Extensions.Logging;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Models;

namespace TelegramGroupsAdmin.Jobs;

/// <summary>
/// Test job to verify TickerQ setup works
/// Runs every 5 minutes as a simple health check
/// </summary>
public class TestJob(ILogger<TestJob> logger)
{
    private readonly ILogger<TestJob> _logger = logger;

    /// <summary>
    /// Simple test ticker that logs every 5 minutes
    /// This verifies TickerQ is working correctly
    /// </summary>
    [TickerFunction(functionName: "TickerQHealthCheck", cronExpression: "*/5 * * * *")]
    public Task HealthCheckAsync(TickerFunctionContext<string> context, CancellationToken cancellationToken)
    {
        _logger.LogInformation("TickerQ health check executed at {Time}", DateTimeOffset.UtcNow);
        return Task.CompletedTask;
    }
}
