using Dapper;
using Npgsql;
using TelegramGroupsAdmin.Models;
using DataModels = TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Repositories;

public class TelegramLinkTokenRepository : ITelegramLinkTokenRepository
{
    private readonly string _connectionString;

    public TelegramLinkTokenRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("PostgreSQL")
            ?? throw new InvalidOperationException("PostgreSQL connection string not found");
    }

    public async Task InsertAsync(TelegramLinkTokenRecord token)
    {
        const string sql = """
            INSERT INTO telegram_link_tokens (token, user_id, created_at, expires_at, used_at, used_by_telegram_id)
            VALUES (@token, @user_id, @created_at, @expires_at, @used_at, @used_by_telegram_id)
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        var dataModel = token.ToDataModel();
        await connection.ExecuteAsync(sql, dataModel);
    }

    public async Task<TelegramLinkTokenRecord?> GetByTokenAsync(string token)
    {
        const string sql = """
            SELECT token, user_id, created_at, expires_at, used_at, used_by_telegram_id
            FROM telegram_link_tokens
            WHERE token = @Token
            LIMIT 1
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        var result = await connection.QuerySingleOrDefaultAsync<DataModels.TelegramLinkTokenRecord>(sql, new { Token = token });
        return result?.ToUiModel();
    }

    public async Task MarkAsUsedAsync(string token, long telegramId)
    {
        const string sql = """
            UPDATE telegram_link_tokens
            SET used_at = @UsedAt,
                used_by_telegram_id = @TelegramId
            WHERE token = @Token
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await connection.ExecuteAsync(sql, new { Token = token, UsedAt = now, TelegramId = telegramId });
    }

    public async Task DeleteExpiredTokensAsync(long beforeTimestamp)
    {
        const string sql = """
            DELETE FROM telegram_link_tokens
            WHERE expires_at < @BeforeTimestamp
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.ExecuteAsync(sql, new { BeforeTimestamp = beforeTimestamp });
    }

    public async Task<IEnumerable<TelegramLinkTokenRecord>> GetActiveTokensForUserAsync(string userId)
    {
        const string sql = """
            SELECT token, user_id, created_at, expires_at, used_at, used_by_telegram_id
            FROM telegram_link_tokens
            WHERE user_id = @UserId
              AND used_at IS NULL
              AND expires_at > @Now
            ORDER BY created_at DESC
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var results = await connection.QueryAsync<DataModels.TelegramLinkTokenRecord>(sql, new { UserId = userId, Now = now });
        return results.Select(r => r.ToUiModel());
    }

    public async Task RevokeUnusedTokensForUserAsync(string userId)
    {
        const string sql = """
            DELETE FROM telegram_link_tokens
            WHERE user_id = @UserId
              AND used_at IS NULL
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.ExecuteAsync(sql, new { UserId = userId });
    }
}
