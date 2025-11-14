using System.Text.Json;
using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using Quartz;
using TelegramGroupsAdmin.Core.Telemetry;
using TelegramGroupsAdmin.Telegram.Abstractions;

namespace TelegramGroupsAdmin.BackgroundJobs.Jobs;

/// <summary>
/// Job logic to run PostgreSQL database maintenance operations (VACUUM, ANALYZE)
/// Executes VACUUM to reclaim storage and ANALYZE to update query planner statistics
/// </summary>
public class DatabaseMaintenanceJob : IJob
{
    private readonly ILogger<DatabaseMaintenanceJob> _logger;
    private readonly IConfiguration _configuration;

    public DatabaseMaintenanceJob(ILogger<DatabaseMaintenanceJob> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        // Extract payload from job data map (deserialize from JSON string)
        // Scheduled triggers don't have payloads, manual triggers do
        DatabaseMaintenancePayload payload;

        if (context.JobDetail.JobDataMap.ContainsKey("payload"))
        {
            // Manual trigger - deserialize provided payload
            var payloadJson = context.JobDetail.JobDataMap.GetString("payload")!;
            payload = JsonSerializer.Deserialize<DatabaseMaintenancePayload>(payloadJson)
                ?? throw new InvalidOperationException("Failed to deserialize DatabaseMaintenancePayload");
        }
        else
        {
            // Scheduled trigger - use default payload (VACUUM + ANALYZE, no VACUUM FULL)
            payload = new DatabaseMaintenancePayload
            {
                RunVacuum = true,
                RunAnalyze = true,
                RunVacuumFull = false
            };
        }

        await ExecuteAsync(payload, context.CancellationToken);
    }

    private async Task ExecuteAsync(DatabaseMaintenancePayload payload, CancellationToken cancellationToken)
    {
        const string jobName = "DatabaseMaintenance";
        var startTimestamp = Stopwatch.GetTimestamp();
        var success = false;

        try
        {
            try
            {
                if (payload == null)
                {
                    _logger.LogError("DatabaseMaintenanceJobLogic received null payload");
                    return;
                }

                var connectionString = _configuration.GetConnectionString("PostgreSQL");
                if (string.IsNullOrEmpty(connectionString))
                {
                    _logger.LogError("PostgreSQL connection string not found in configuration");
                    return;
                }

                _logger.LogInformation("Database maintenance job started");

                // VACUUM and ANALYZE must run outside of transactions
                // NpgsqlConnection with Enlist=false ensures no automatic transaction enrollment
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync(cancellationToken);

                // Execute maintenance operations based on payload
                if (payload.RunVacuumFull)
                {
                    _logger.LogWarning("Running VACUUM FULL - this will lock tables and may take significant time");
                    await ExecuteMaintenanceCommandAsync(connection, "VACUUM FULL ANALYZE;", cancellationToken);
                }
                else
                {
                    // Standard VACUUM and ANALYZE (can run separately or together)
                    if (payload.RunVacuum && payload.RunAnalyze)
                    {
                        _logger.LogInformation("Running VACUUM ANALYZE to reclaim storage and update statistics");
                        await ExecuteMaintenanceCommandAsync(connection, "VACUUM ANALYZE;", cancellationToken);
                    }
                    else if (payload.RunVacuum)
                    {
                        _logger.LogInformation("Running VACUUM to reclaim storage from dead tuples");
                        await ExecuteMaintenanceCommandAsync(connection, "VACUUM;", cancellationToken);
                    }
                    else if (payload.RunAnalyze)
                    {
                        _logger.LogInformation("Running ANALYZE to update query planner statistics");
                        await ExecuteMaintenanceCommandAsync(connection, "ANALYZE;", cancellationToken);
                    }
                }

                _logger.LogInformation("Database maintenance job completed successfully");
                success = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during database maintenance");
                throw; // Re-throw for retry logic and exception recording
            }
        }
        finally
        {
            var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

            // Record metrics (using TagList to avoid boxing/allocations)
            var tags = new TagList
            {
                { "job_name", jobName },
                { "status", success ? "success" : "failure" }
            };

            TelemetryConstants.JobExecutions.Add(1, tags);
            TelemetryConstants.JobDuration.Record(elapsedMs, new TagList { { "job_name", jobName } });
        }
    }

    private async Task ExecuteMaintenanceCommandAsync(NpgsqlConnection connection, string command, CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;

        await using var cmd = new NpgsqlCommand(command, connection);
        cmd.CommandTimeout = 600; // 10 minutes - maintenance can take time on large databases

        await cmd.ExecuteNonQueryAsync(cancellationToken);

        var duration = DateTime.UtcNow - startTime;
        _logger.LogInformation("Command '{Command}' completed in {Duration:F2} seconds",
            command.TrimEnd(';'), duration.TotalSeconds);
    }
}
