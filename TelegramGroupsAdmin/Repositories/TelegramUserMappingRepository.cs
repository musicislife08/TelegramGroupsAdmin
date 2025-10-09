using Dapper;
using Npgsql;
using TelegramGroupsAdmin.Models;
using DataModels = TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Repositories;

public class TelegramUserMappingRepository : ITelegramUserMappingRepository
{
    private readonly string _connectionString;

    public TelegramUserMappingRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<IEnumerable<TelegramUserMappingRecord>> GetByUserIdAsync(string userId)
    {
        const string sql = """
            SELECT id, telegram_id, telegram_username, user_id, linked_at, is_active
            FROM telegram_user_mappings
            WHERE user_id = @UserId AND is_active = true
            ORDER BY linked_at DESC
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        var results = await connection.QueryAsync<DataModels.TelegramUserMappingRecord>(sql, new { UserId = userId });
        return results.Select(r => r.ToUiModel());
    }

    public async Task<TelegramUserMappingRecord?> GetByTelegramIdAsync(long telegramId)
    {
        const string sql = """
            SELECT id, telegram_id, telegram_username, user_id, linked_at, is_active
            FROM telegram_user_mappings
            WHERE telegram_id = @TelegramId AND is_active = true
            LIMIT 1
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        var result = await connection.QuerySingleOrDefaultAsync<DataModels.TelegramUserMappingRecord>(sql, new { TelegramId = telegramId });
        return result?.ToUiModel();
    }

    public async Task<string?> GetUserIdByTelegramIdAsync(long telegramId)
    {
        const string sql = """
            SELECT user_id
            FROM telegram_user_mappings
            WHERE telegram_id = @TelegramId AND is_active = true
            LIMIT 1
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        return await connection.QuerySingleOrDefaultAsync<string?>(sql, new { TelegramId = telegramId });
    }

    public async Task<long> InsertAsync(TelegramUserMappingRecord mapping)
    {
        const string sql = """
            INSERT INTO telegram_user_mappings (telegram_id, telegram_username, user_id, linked_at, is_active)
            VALUES (@telegram_id, @telegram_username, @user_id, @linked_at, @is_active)
            RETURNING id
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        var dataModel = mapping.ToDataModel();
        return await connection.ExecuteScalarAsync<long>(sql, dataModel);
    }

    public async Task<bool> DeactivateAsync(long mappingId)
    {
        const string sql = """
            UPDATE telegram_user_mappings
            SET is_active = false
            WHERE id = @Id
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        var rowsAffected = await connection.ExecuteAsync(sql, new { Id = mappingId });
        return rowsAffected > 0;
    }

    public async Task<bool> IsTelegramIdLinkedAsync(long telegramId)
    {
        const string sql = """
            SELECT EXISTS(
                SELECT 1
                FROM telegram_user_mappings
                WHERE telegram_id = @TelegramId AND is_active = true
            )
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        return await connection.ExecuteScalarAsync<bool>(sql, new { TelegramId = telegramId });
    }
}
