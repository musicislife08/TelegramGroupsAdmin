using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Data.Repositories;

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
        var dbPath = configuration["Identity:DatabasePath"] ?? "/data/identity.db";
        _connectionString = $"Data Source={dbPath}";
        _logger = logger;
    }

    public async Task LogEventAsync(
        AuditEventType eventType,
        string? actorUserId,
        string? targetUserId = null,
        string? value = null,
        CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);

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

    public async Task<List<AuditLogRecord>> GetRecentEventsAsync(int limit = 100, CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);

        const string sql = """
            SELECT id, event_type, timestamp, actor_user_id, target_user_id, value
            FROM audit_log
            ORDER BY timestamp DESC
            LIMIT @Limit;
            """;

        var dtos = await connection.QueryAsync<AuditLogRecordDto>(sql, new { Limit = limit });
        return dtos.Select(dto => dto.ToAuditLogRecord()).ToList();
    }

    public async Task<List<AuditLogRecord>> GetEventsForUserAsync(string userId, int limit = 100, CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);

        const string sql = """
            SELECT id, event_type, timestamp, actor_user_id, target_user_id, value
            FROM audit_log
            WHERE target_user_id = @UserId
            ORDER BY timestamp DESC
            LIMIT @Limit;
            """;

        var dtos = await connection.QueryAsync<AuditLogRecordDto>(sql, new { UserId = userId, Limit = limit });
        return dtos.Select(dto => dto.ToAuditLogRecord()).ToList();
    }

    public async Task<List<AuditLogRecord>> GetEventsByActorAsync(string actorUserId, int limit = 100, CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);

        const string sql = """
            SELECT id, event_type, timestamp, actor_user_id, target_user_id, value
            FROM audit_log
            WHERE actor_user_id = @ActorUserId
            ORDER BY timestamp DESC
            LIMIT @Limit;
            """;

        var dtos = await connection.QueryAsync<AuditLogRecordDto>(sql, new { ActorUserId = actorUserId, Limit = limit });
        return dtos.Select(dto => dto.ToAuditLogRecord()).ToList();
    }

    public async Task<List<AuditLogRecord>> GetEventsByTypeAsync(AuditEventType eventType, int limit = 100, CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);

        const string sql = """
            SELECT id, event_type, timestamp, actor_user_id, target_user_id, value
            FROM audit_log
            WHERE event_type = @EventType
            ORDER BY timestamp DESC
            LIMIT @Limit;
            """;

        var dtos = await connection.QueryAsync<AuditLogRecordDto>(sql, new { EventType = (int)eventType, Limit = limit });
        return dtos.Select(dto => dto.ToAuditLogRecord()).ToList();
    }
}
