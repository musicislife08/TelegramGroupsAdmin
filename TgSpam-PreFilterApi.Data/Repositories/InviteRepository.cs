using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TgSpam_PreFilterApi.Data.Models;

namespace TgSpam_PreFilterApi.Data.Repositories;

public class InviteRepository
{
    private readonly string _connectionString;
    private readonly ILogger<InviteRepository> _logger;

    public InviteRepository(IConfiguration configuration, ILogger<InviteRepository> logger)
    {
        var dbPath = configuration["Identity:DatabasePath"] ?? "/data/identity.db";
        _connectionString = $"Data Source={dbPath}";
        _logger = logger;
    }

    public async Task<InviteRecord?> GetByTokenAsync(string token)
    {
        await using var connection = new SqliteConnection(_connectionString);

        const string sql = """
            SELECT token AS Token, created_by AS CreatedBy, created_at AS CreatedAt,
                   expires_at AS ExpiresAt, used_by AS UsedBy, used_at AS UsedAt
            FROM invites
            WHERE token = @Token;
            """;

        return await connection.QueryFirstOrDefaultAsync<InviteRecord>(sql, new { Token = token });
    }

    public async Task<string> CreateAsync(string createdBy, int validDays = 7)
    {
        await using var connection = new SqliteConnection(_connectionString);

        var token = Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var expiresAt = now + (validDays * 24 * 3600);

        const string sql = """
            INSERT INTO invites (token, created_by, created_at, expires_at)
            VALUES (@Token, @CreatedBy, @CreatedAt, @ExpiresAt);
            """;

        await connection.ExecuteAsync(sql, new
        {
            Token = token,
            CreatedBy = createdBy,
            CreatedAt = now,
            ExpiresAt = expiresAt
        });

        _logger.LogInformation("Created invite {Token} by user {CreatedBy}, expires at {ExpiresAt}",
            token, createdBy, DateTimeOffset.FromUnixTimeSeconds(expiresAt));

        return token;
    }

    public async Task MarkAsUsedAsync(string token, string usedBy)
    {
        await using var connection = new SqliteConnection(_connectionString);

        const string sql = """
            UPDATE invites
            SET used_by = @UsedBy, used_at = @Now
            WHERE token = @Token;
            """;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await connection.ExecuteAsync(sql, new { Token = token, UsedBy = usedBy, Now = now });

        _logger.LogInformation("Invite {Token} used by user {UsedBy}", token, usedBy);
    }

    public async Task<List<InviteRecord>> GetByCreatorAsync(string createdBy)
    {
        await using var connection = new SqliteConnection(_connectionString);

        const string sql = """
            SELECT token AS Token, created_by AS CreatedBy, created_at AS CreatedAt,
                   expires_at AS ExpiresAt, used_by AS UsedBy, used_at AS UsedAt
            FROM invites
            WHERE created_by = @CreatedBy
            ORDER BY created_at DESC;
            """;

        var invites = await connection.QueryAsync<InviteRecord>(sql, new { CreatedBy = createdBy });
        return invites.ToList();
    }

    public async Task<int> CleanupExpiredAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);

        const string sql = """
            DELETE FROM invites
            WHERE expires_at <= @Now AND used_by IS NULL;
            """;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var deleted = await connection.ExecuteAsync(sql, new { Now = now });

        if (deleted > 0)
        {
            _logger.LogInformation("Cleaned up {Count} expired invites", deleted);
        }

        return deleted;
    }
}
