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

public class VerificationTokenRepository
{
    private readonly string _connectionString;
    private readonly ILogger<VerificationTokenRepository> _logger;

    public VerificationTokenRepository(IConfiguration configuration, ILogger<VerificationTokenRepository> logger)
    {
        var dbPath = configuration["Identity:DatabasePath"] ?? "/data/identity.db";
        _connectionString = $"Data Source={dbPath}";
        _logger = logger;
    }

    public async Task<long> CreateAsync(VerificationToken verificationToken, CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);

        const string sql = """
            INSERT INTO verification_tokens (
                user_id, token_type, token, value, expires_at, created_at, used_at
            ) VALUES (
                @UserId, @TokenType, @Token, @Value, @ExpiresAt, @CreatedAt, @UsedAt
            );
            SELECT last_insert_rowid();
            """;

        var id = await connection.ExecuteScalarAsync<long>(sql, new
        {
            verificationToken.UserId,
            TokenType = verificationToken.TokenTypeString,
            verificationToken.Token,
            verificationToken.Value,
            verificationToken.ExpiresAt,
            verificationToken.CreatedAt,
            verificationToken.UsedAt
        });

        _logger.LogDebug("Created verification token {Id} for user {UserId}, type {TokenType}",
            id, verificationToken.UserId, verificationToken.TokenType);

        return id;
    }

    public async Task<VerificationToken?> GetByTokenAsync(string token, CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);

        const string sql = """
            SELECT id, user_id, token_type, token, value, expires_at, created_at, used_at
            FROM verification_tokens
            WHERE token = @Token;
            """;

        var dto = await connection.QueryFirstOrDefaultAsync<VerificationTokenDto>(sql, new { Token = token });
        return dto?.ToVerificationToken();
    }

    public async Task<VerificationToken?> GetValidTokenAsync(string token, TokenType tokenType, CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);

        var tokenTypeString = tokenType switch
        {
            TokenType.EmailVerification => "email_verify",
            TokenType.PasswordReset => "password_reset",
            TokenType.EmailChange => "email_change",
            _ => throw new ArgumentException($"Unknown token type: {tokenType}")
        };

        const string sql = """
            SELECT id, user_id, token_type, token, value, expires_at, created_at, used_at
            FROM verification_tokens
            WHERE token = @Token
              AND token_type = @TokenType
              AND used_at IS NULL
              AND expires_at > @Now;
            """;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var dto = await connection.QueryFirstOrDefaultAsync<VerificationTokenDto>(sql, new
        {
            Token = token,
            TokenType = tokenTypeString,
            Now = now
        });

        return dto?.ToVerificationToken();
    }

    public async Task MarkAsUsedAsync(string token, CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);

        const string sql = """
            UPDATE verification_tokens
            SET used_at = @Now
            WHERE token = @Token;
            """;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await connection.ExecuteAsync(sql, new { Token = token, Now = now });

        _logger.LogDebug("Marked verification token as used: {Token}", token);
    }

    public async Task<int> CleanupExpiredAsync(CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);

        const string sql = """
            DELETE FROM verification_tokens
            WHERE expires_at <= @Now;
            """;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var deleted = await connection.ExecuteAsync(sql, new { Now = now });

        if (deleted > 0)
        {
            _logger.LogInformation("Cleaned up {Count} expired verification tokens", deleted);
        }

        return deleted;
    }

    public async Task<int> DeleteByUserIdAsync(string userId, CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);

        const string sql = """
            DELETE FROM verification_tokens
            WHERE user_id = @UserId;
            """;

        var deleted = await connection.ExecuteAsync(sql, new { UserId = userId });

        if (deleted > 0)
        {
            _logger.LogDebug("Deleted {Count} verification tokens for user {UserId}", deleted, userId);
        }

        return deleted;
    }
}
