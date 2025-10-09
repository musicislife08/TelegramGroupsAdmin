using Dapper;
using Npgsql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.Models;

namespace TelegramGroupsAdmin.Repositories;

// CRITICAL DAPPER/DTO CONVENTION:
// All SQL SELECT statements MUST use raw snake_case column names without aliases.
// DTOs use positional record constructors that are CASE-SENSITIVE.
// See MessageRecord.cs for detailed explanation.

public class AuditLogRepository
{
    private readonly string _connectionString;
    private readonly ILogger<AuditLogRepository> _logger;

    public AuditLogRepository(IConfiguration configuration, ILogger<AuditLogRepository> logger)
    {
        _connectionString = configuration.GetConnectionString("PostgreSQL")
            ?? throw new InvalidOperationException("PostgreSQL connection string not found");
        _logger = logger;
    }

    public async Task LogEventAsync(
        DataModels.AuditEventType eventType,
        string? actorUserId,
        string? targetUserId = null,
        string? value = null,
        CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        const string sql = """
            INSERT INTO audit_log (
                event_type, timestamp, actor_user_id, target_user_id, value
            ) VALUES (
                @EventType, @Timestamp, @ActorUserId, @TargetUserId, @Value
            );
            """;

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        await connection.ExecuteAsync(sql, new
        {
            EventType = (int)eventType,
            Timestamp = timestamp,
            ActorUserId = actorUserId,
            TargetUserId = targetUserId,
            Value = value
        });

        _logger.LogInformation("Audit log: {EventType} by {ActorUserId} on {TargetUserId}",
            eventType, actorUserId ?? "SYSTEM", targetUserId ?? "N/A");
    }

    public async Task<List<UiModels.AuditLogRecord>> GetRecentEventsAsync(int limit = 100, CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        const string sql = """
            SELECT id, event_type, timestamp, actor_user_id, target_user_id, value
            FROM audit_log
            ORDER BY timestamp DESC
            LIMIT @Limit;
            """;

        var dtos = await connection.QueryAsync<DataModels.AuditLogRecordDto>(sql, new { Limit = limit });
        return dtos.Select(dto => dto.ToAuditLogRecord().ToUiModel()).ToList();
    }

    public async Task<List<UiModels.AuditLogRecord>> GetEventsForUserAsync(string userId, int limit = 100, CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        const string sql = """
            SELECT id, event_type, timestamp, actor_user_id, target_user_id, value
            FROM audit_log
            WHERE target_user_id = @UserId
            ORDER BY timestamp DESC
            LIMIT @Limit;
            """;

        var dtos = await connection.QueryAsync<DataModels.AuditLogRecordDto>(sql, new { UserId = userId, Limit = limit });
        return dtos.Select(dto => dto.ToAuditLogRecord().ToUiModel()).ToList();
    }

    public async Task<List<UiModels.AuditLogRecord>> GetEventsByActorAsync(string actorUserId, int limit = 100, CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        const string sql = """
            SELECT id, event_type, timestamp, actor_user_id, target_user_id, value
            FROM audit_log
            WHERE actor_user_id = @ActorUserId
            ORDER BY timestamp DESC
            LIMIT @Limit;
            """;

        var dtos = await connection.QueryAsync<DataModels.AuditLogRecordDto>(sql, new { ActorUserId = actorUserId, Limit = limit });
        return dtos.Select(dto => dto.ToAuditLogRecord().ToUiModel()).ToList();
    }

    public async Task<List<UiModels.AuditLogRecord>> GetEventsByTypeAsync(DataModels.AuditEventType eventType, int limit = 100, CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        const string sql = """
            SELECT id, event_type, timestamp, actor_user_id, target_user_id, value
            FROM audit_log
            WHERE event_type = @EventType
            ORDER BY timestamp DESC
            LIMIT @Limit;
            """;

        var dtos = await connection.QueryAsync<DataModels.AuditLogRecordDto>(sql, new { EventType = (int)eventType, Limit = limit });
        return dtos.Select(dto => dto.ToAuditLogRecord().ToUiModel()).ToList();
    }
}
