using Dapper;
using Npgsql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Models;
using DataModels = TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Repositories;

public class UserActionsRepository : IUserActionsRepository
{
    private readonly string _connectionString;
    private readonly ILogger<UserActionsRepository> _logger;

    public UserActionsRepository(
        IConfiguration configuration,
        ILogger<UserActionsRepository> logger)
    {
        _connectionString = configuration.GetConnectionString("PostgreSQL")
            ?? throw new InvalidOperationException("PostgreSQL connection string not found");
        _logger = logger;
    }

    public async Task<long> InsertAsync(UserActionRecord action)
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        const string sql = """
            INSERT INTO user_actions (
                user_id, action_type, message_id,
                issued_by, issued_at, expires_at, reason
            ) VALUES (
                @UserId, @ActionType, @MessageId,
                @IssuedBy, @IssuedAt, @ExpiresAt, @Reason
            )
            RETURNING id;
            """;

        var id = await connection.ExecuteScalarAsync<long>(sql, new
        {
            action.UserId,
            action.ActionType,
            action.MessageId,
            action.IssuedBy,
            action.IssuedAt,
            action.ExpiresAt,
            action.Reason
        });

        _logger.LogInformation(
            "Inserted user action {ActionType} for user {UserId} (expires: {ExpiresAt})",
            action.ActionType,
            action.UserId,
            action.ExpiresAt?.ToString() ?? "never");

        return id;
    }

    public async Task<UserActionRecord?> GetByIdAsync(long id)
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        const string sql = """
            SELECT id, user_id, action_type, message_id,
                   issued_by, issued_at, expires_at, reason
            FROM user_actions
            WHERE id = @Id;
            """;

        var dto = await connection.QuerySingleOrDefaultAsync<DataModels.UserActionRecordDto>(
            sql,
            new { Id = id });

        return dto?.ToUserActionRecord().ToUiModel();
    }

    public async Task<List<UserActionRecord>> GetByUserIdAsync(long userId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        const string sql = """
            SELECT id, user_id, action_type, message_id,
                   issued_by, issued_at, expires_at, reason
            FROM user_actions
            WHERE user_id = @UserId
            ORDER BY issued_at DESC;
            """;

        var dtos = await connection.QueryAsync<DataModels.UserActionRecordDto>(
            sql,
            new { UserId = userId });

        return dtos.Select(dto => dto.ToUserActionRecord().ToUiModel()).ToList();
    }

    public async Task<List<UserActionRecord>> GetActiveActionsByUserIdAsync(long userId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        const string sql = """
            SELECT id, user_id, action_type, message_id,
                   issued_by, issued_at, expires_at, reason
            FROM user_actions
            WHERE user_id = @UserId
              AND (expires_at IS NULL OR expires_at > @Now)
            ORDER BY issued_at DESC;
            """;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var dtos = await connection.QueryAsync<DataModels.UserActionRecordDto>(
            sql,
            new { UserId = userId, Now = now });

        return dtos.Select(dto => dto.ToUserActionRecord().ToUiModel()).ToList();
    }

    public async Task<List<UserActionRecord>> GetActiveBansAsync()
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        const string sql = """
            SELECT id, user_id, action_type, message_id,
                   issued_by, issued_at, expires_at, reason
            FROM user_actions
            WHERE action_type = @ActionType
              AND (expires_at IS NULL OR expires_at > @Now)
            ORDER BY issued_at DESC;
            """;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var dtos = await connection.QueryAsync<DataModels.UserActionRecordDto>(
            sql,
            new { ActionType = (int)Models.UserActionType.Ban, Now = now });

        return dtos.Select(dto => dto.ToUserActionRecord().ToUiModel()).ToList();
    }

    public async Task<bool> IsUserBannedAsync(long userId, long? chatId = null)
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        // Check for active ban (all bans are global now)
        const string sql = """
            SELECT EXISTS (
                SELECT 1 FROM user_actions
                WHERE user_id = @UserId
                  AND action_type = @ActionType
                  AND (expires_at IS NULL OR expires_at > @Now)
            );
            """;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var isBanned = await connection.ExecuteScalarAsync<bool>(
            sql,
            new { UserId = userId, Now = now, ActionType = (int)Models.UserActionType.Ban });

        return isBanned;
    }

    public async Task<bool> IsUserTrustedAsync(long userId, long? chatId = null)
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        // Check for active 'trust' action (all trusts are global now)
        const string sql = """
            SELECT EXISTS (
                SELECT 1 FROM user_actions
                WHERE user_id = @UserId
                  AND action_type = @ActionType
                  AND (expires_at IS NULL OR expires_at > @Now)
            );
            """;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var isTrusted = await connection.ExecuteScalarAsync<bool>(
            sql,
            new { UserId = userId, Now = now, ActionType = (int)Models.UserActionType.Trust });

        return isTrusted;
    }

    public async Task<int> GetWarnCountAsync(long userId, long? chatId = null)
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        // Count active warns for user (all warns are global now)
        const string sql = """
            SELECT COUNT(*)
            FROM user_actions
            WHERE user_id = @UserId
              AND action_type = @ActionType
              AND (expires_at IS NULL OR expires_at > @Now);
            """;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var count = await connection.ExecuteScalarAsync<int>(
            sql,
            new { UserId = userId, Now = now, ActionType = (int)Models.UserActionType.Warn });

        return count;
    }

    public async Task ExpireActionAsync(long actionId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        const string sql = """
            UPDATE user_actions
            SET expires_at = @Now
            WHERE id = @ActionId;
            """;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await connection.ExecuteAsync(sql, new { ActionId = actionId, Now = now });

        _logger.LogDebug("Expired action {ActionId}", actionId);
    }

    public async Task ExpireBansForUserAsync(long userId, long? chatId = null)
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        // Expire all active bans for user (all bans are global now)
        const string sql = """
            UPDATE user_actions
            SET expires_at = @Now
            WHERE user_id = @UserId
              AND action_type = @ActionType
              AND (expires_at IS NULL OR expires_at > @Now);
            """;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var count = await connection.ExecuteAsync(
            sql,
            new { UserId = userId, Now = now, ActionType = (int)Models.UserActionType.Ban });

        _logger.LogInformation(
            "Expired {Count} bans for user {UserId}",
            count,
            userId);
    }

    public async Task ExpireTrustsForUserAsync(long userId, long? chatId = null)
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        // Expire all active trusts for user (all trusts are global now)
        const string sql = """
            UPDATE user_actions
            SET expires_at = @Now
            WHERE user_id = @UserId
              AND action_type = @ActionType
              AND (expires_at IS NULL OR expires_at > @Now);
            """;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var count = await connection.ExecuteAsync(
            sql,
            new { UserId = userId, Now = now, ActionType = (int)Models.UserActionType.Trust });

        _logger.LogInformation(
            "Expired {Count} trusts for user {UserId}",
            count,
            userId);
    }

    public async Task<List<UserActionRecord>> GetRecentAsync(int limit = 100)
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        const string sql = """
            SELECT id, user_id, action_type, message_id,
                   issued_by, issued_at, expires_at, reason
            FROM user_actions
            ORDER BY issued_at DESC
            LIMIT @Limit;
            """;

        var dtos = await connection.QueryAsync<DataModels.UserActionRecordDto>(
            sql,
            new { Limit = limit });

        return dtos.Select(dto => dto.ToUserActionRecord().ToUiModel()).ToList();
    }

    public async Task<int> DeleteOlderThanAsync(long timestamp)
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        // Delete old actions (e.g., expired warns older than 1 year)
        const string sql = """
            DELETE FROM user_actions
            WHERE issued_at < @Timestamp
              AND (expires_at IS NOT NULL AND expires_at < @Timestamp);
            """;

        var deleted = await connection.ExecuteAsync(sql, new { Timestamp = timestamp });

        if (deleted > 0)
        {
            _logger.LogInformation(
                "Deleted {Count} old user actions (issued before {Timestamp})",
                deleted,
                timestamp);
        }

        return deleted;
    }

    public async Task<List<UserActionRecord>> GetActiveActionsAsync(long userId, UserActionType actionType)
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        const string sql = """
            SELECT id, user_id, action_type, message_id,
                   issued_by, issued_at, expires_at, reason
            FROM user_actions
            WHERE user_id = @UserId
              AND action_type = @ActionType
              AND (expires_at IS NULL OR expires_at > @Now);
            """;

        var dtos = await connection.QueryAsync<DataModels.UserActionRecordDto>(
            sql,
            new { UserId = userId, ActionType = (int)actionType, Now = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });

        return dtos.Select(dto => dto.ToUserActionRecord().ToUiModel()).ToList();
    }

    public async Task DeactivateAsync(long actionId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        // Soft delete by setting expires_at to now
        const string sql = """
            UPDATE user_actions
            SET expires_at = @Now
            WHERE id = @Id;
            """;

        await connection.ExecuteAsync(sql, new { Id = actionId, Now = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });

        _logger.LogDebug("Deactivated user action {ActionId}", actionId);
    }
}
