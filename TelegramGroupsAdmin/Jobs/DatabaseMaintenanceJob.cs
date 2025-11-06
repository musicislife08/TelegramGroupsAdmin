using Npgsql;
using TelegramGroupsAdmin.Telegram.Abstractions;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Models;

namespace TelegramGroupsAdmin.Jobs;

/// <summary>
/// Scheduled job to run PostgreSQL database maintenance operations (VACUUM, ANALYZE)
/// Executes VACUUM to reclaim storage and ANALYZE to update query planner statistics
/// </summary>
public class DatabaseMaintenanceJob
{
    private readonly ILogger<DatabaseMaintenanceJob> _logger;
    private readonly IConfiguration _configuration;

    public DatabaseMaintenanceJob(ILogger<DatabaseMaintenanceJob> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    [TickerFunction("database_maintenance")]
    public async Task ExecuteAsync(TickerFunctionContext<DatabaseMaintenancePayload> context, CancellationToken cancellationToken)
    {
        try
        {
            var payload = context.Request;
            if (payload == null)
            {
                _logger.LogError("DatabaseMaintenanceJob received null payload");
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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during database maintenance");
            throw; // Re-throw for TickerQ retry logic
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
