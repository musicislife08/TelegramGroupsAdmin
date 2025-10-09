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
                user_id, chat_ids, action_type, message_id,
                issued_by, issued_at, expires_at, reason
            ) VALUES (
                @UserId, @ChatIds, @ActionType, @MessageId,
                @IssuedBy, @IssuedAt, @ExpiresAt, @Reason
            )
            RETURNING id;
            """;

        var id = await connection.ExecuteScalarAsync<long>(sql, new
        {
            action.UserId,
            action.ChatIds,
            action.ActionType,
            action.MessageId,
            action.IssuedBy,
            action.IssuedAt,
            action.ExpiresAt,
            action.Reason
        });

        _logger.LogInformation(
            "Inserted user action {ActionType} for user {UserId} (chat: {ChatIds}, expires: {ExpiresAt})",
            action.ActionType,
            action.UserId,
            action.ChatIds != null ? string.Join(",", action.ChatIds) : "global",
            action.ExpiresAt?.ToString() ?? "never");

        return id;
    }

    public async Task<UserActionRecord?> GetByIdAsync(long id)
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        const string sql = """
            SELECT id, user_id, chat_ids, action_type, message_id,
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
            SELECT id, user_id, chat_ids, action_type, message_id,
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
            SELECT id, user_id, chat_ids, action_type, message_id,
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
            SELECT id, user_id, chat_ids, action_type, message_id,
                   issued_by, issued_at, expires_at, reason
            FROM user_actions
            WHERE action_type = 'ban'
              AND (expires_at IS NULL OR expires_at > @Now)
            ORDER BY issued_at DESC;
            """;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var dtos = await connection.QueryAsync<DataModels.UserActionRecordDto>(
            sql,
            new { Now = now });

        return dtos.Select(dto => dto.ToUserActionRecord().ToUiModel()).ToList();
    }

    public async Task<bool> IsUserBannedAsync(long userId, long? chatId = null)
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        // Check for active ban
        // chatId NULL = check global ban, otherwise check specific chat or global
        const string sql = """
            SELECT EXISTS (
                SELECT 1 FROM user_actions
                WHERE user_id = @UserId
                  AND action_type = 'ban'
                  AND (expires_at IS NULL OR expires_at > @Now)
                  AND (
                      chat_ids IS NULL
                      OR @ChatId = ANY(chat_ids)
                  )
            );
            """;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var isBanned = await connection.ExecuteScalarAsync<bool>(
            sql,
            new { UserId = userId, ChatId = chatId, Now = now });

        return isBanned;
    }

    public async Task<bool> IsUserTrustedAsync(long userId, long? chatId = null)
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        // Check for active 'trust' action
        const string sql = """
            SELECT EXISTS (
                SELECT 1 FROM user_actions
                WHERE user_id = @UserId
                  AND action_type = 'trust'
                  AND (expires_at IS NULL OR expires_at > @Now)
                  AND (
                      chat_ids IS NULL
                      OR @ChatId = ANY(chat_ids)
                  )
            );
            """;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var isTrusted = await connection.ExecuteScalarAsync<bool>(
            sql,
            new { UserId = userId, ChatId = chatId, Now = now });

        return isTrusted;
    }

    public async Task<int> GetWarnCountAsync(long userId, long? chatId = null)
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        // Count active warns for user
        const string sql = """
            SELECT COUNT(*)
            FROM user_actions
            WHERE user_id = @UserId
              AND action_type = 'warn'
              AND (expires_at IS NULL OR expires_at > @Now)
              AND (
                  chat_ids IS NULL
                  OR @ChatId = ANY(chat_ids)
              );
            """;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var count = await connection.ExecuteScalarAsync<int>(
            sql,
            new { UserId = userId, ChatId = chatId, Now = now });

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

        // Expire all active bans for user
        const string sql = """
            UPDATE user_actions
            SET expires_at = @Now
            WHERE user_id = @UserId
              AND action_type = 'ban'
              AND (expires_at IS NULL OR expires_at > @Now)
              AND (
                  @ChatId IS NULL
                  OR chat_ids IS NULL
                  OR @ChatId = ANY(chat_ids)
              );
            """;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var count = await connection.ExecuteAsync(
            sql,
            new { UserId = userId, ChatId = chatId, Now = now });

        _logger.LogInformation(
            "Expired {Count} bans for user {UserId} (chat: {ChatId})",
            count,
            userId,
            chatId?.ToString() ?? "all");
    }

    public async Task ExpireTrustsForUserAsync(long userId, long? chatId = null)
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        const string sql = """
            UPDATE user_actions
            SET expires_at = @Now
            WHERE user_id = @UserId
              AND action_type = 'trust'
              AND (expires_at IS NULL OR expires_at > @Now)
              AND (
                  @ChatId IS NULL
                  OR chat_ids IS NULL
                  OR @ChatId = ANY(chat_ids)
              );
            """;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var count = await connection.ExecuteAsync(
            sql,
            new { UserId = userId, ChatId = chatId, Now = now });

        _logger.LogInformation(
            "Expired {Count} trusts for user {UserId} (chat: {ChatId})",
            count,
            userId,
            chatId?.ToString() ?? "all");
    }

    public async Task<List<UserActionRecord>> GetRecentAsync(int limit = 100)
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        const string sql = """
            SELECT id, user_id, chat_ids, action_type, message_id,
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

    public async Task<List<UserActionRecord>> GetByChatIdAsync(long chatId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        const string sql = """
            SELECT id, user_id, chat_ids, action_type, message_id,
                   issued_by, issued_at, expires_at, reason
            FROM user_actions
            WHERE chat_ids IS NULL
               OR @ChatId = ANY(chat_ids)
            ORDER BY issued_at DESC;
            """;

        var dtos = await connection.QueryAsync<DataModels.UserActionRecordDto>(
            sql,
            new { ChatId = chatId });

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
}
