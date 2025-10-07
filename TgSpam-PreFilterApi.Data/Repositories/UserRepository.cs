using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TgSpam_PreFilterApi.Data.Models;

namespace TgSpam_PreFilterApi.Data.Repositories;

public class UserRepository
{
    private readonly string _connectionString;
    private readonly ILogger<UserRepository> _logger;

    public UserRepository(IConfiguration configuration, ILogger<UserRepository> logger)
    {
        var dbPath = configuration["Identity:DatabasePath"] ?? "/data/identity.db";
        _connectionString = $"Data Source={dbPath}";
        _logger = logger;
    }

    public async Task<UserRecord?> GetByEmailAsync(string normalizedEmail)
    {
        await using var connection = new SqliteConnection(_connectionString);

        const string sql = """
            SELECT id AS Id, email AS Email, normalized_email AS NormalizedEmail,
                   password_hash AS PasswordHash, security_stamp AS SecurityStamp,
                   permission_level AS PermissionLevel, invited_by AS InvitedBy,
                   is_active AS IsActive, totp_secret AS TotpSecret,
                   totp_enabled AS TotpEnabled, created_at AS CreatedAt,
                   last_login_at AS LastLoginAt
            FROM users
            WHERE normalized_email = @NormalizedEmail AND is_active = 1;
            """;

        return await connection.QueryFirstOrDefaultAsync<UserRecord>(sql, new { NormalizedEmail = normalizedEmail });
    }

    public async Task<UserRecord?> GetByIdAsync(string userId)
    {
        await using var connection = new SqliteConnection(_connectionString);

        const string sql = """
            SELECT id AS Id, email AS Email, normalized_email AS NormalizedEmail,
                   password_hash AS PasswordHash, security_stamp AS SecurityStamp,
                   permission_level AS PermissionLevel, invited_by AS InvitedBy,
                   is_active AS IsActive, totp_secret AS TotpSecret,
                   totp_enabled AS TotpEnabled, created_at AS CreatedAt,
                   last_login_at AS LastLoginAt
            FROM users
            WHERE id = @UserId;
            """;

        return await connection.QueryFirstOrDefaultAsync<UserRecord>(sql, new { UserId = userId });
    }

    public async Task<string> CreateAsync(UserRecord user)
    {
        await using var connection = new SqliteConnection(_connectionString);

        const string sql = """
            INSERT INTO users (
                id, email, normalized_email, password_hash, security_stamp,
                permission_level, invited_by, is_active, totp_secret, totp_enabled,
                created_at, last_login_at
            ) VALUES (
                @Id, @Email, @NormalizedEmail, @PasswordHash, @SecurityStamp,
                @PermissionLevel, @InvitedBy, @IsActive, @TotpSecret, @TotpEnabled,
                @CreatedAt, @LastLoginAt
            );
            """;

        await connection.ExecuteAsync(sql, user);

        _logger.LogInformation("Created user {Email} with ID {UserId}", user.Email, user.Id);

        return user.Id;
    }

    public async Task UpdateLastLoginAsync(string userId)
    {
        await using var connection = new SqliteConnection(_connectionString);

        const string sql = """
            UPDATE users
            SET last_login_at = @Now
            WHERE id = @UserId;
            """;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await connection.ExecuteAsync(sql, new { UserId = userId, Now = now });
    }

    public async Task UpdateSecurityStampAsync(string userId, string securityStamp)
    {
        await using var connection = new SqliteConnection(_connectionString);

        const string sql = """
            UPDATE users
            SET security_stamp = @SecurityStamp
            WHERE id = @UserId;
            """;

        await connection.ExecuteAsync(sql, new { UserId = userId, SecurityStamp = securityStamp });
    }

    public async Task EnableTotpAsync(string userId, string totpSecret)
    {
        await using var connection = new SqliteConnection(_connectionString);

        const string sql = """
            UPDATE users
            SET totp_secret = @TotpSecret, totp_enabled = 1
            WHERE id = @UserId;
            """;

        await connection.ExecuteAsync(sql, new { UserId = userId, TotpSecret = totpSecret });

        _logger.LogInformation("Enabled TOTP for user {UserId}", userId);
    }

    public async Task DisableTotpAsync(string userId)
    {
        await using var connection = new SqliteConnection(_connectionString);

        const string sql = """
            UPDATE users
            SET totp_secret = NULL, totp_enabled = 0
            WHERE id = @UserId;
            """;

        await connection.ExecuteAsync(sql, new { UserId = userId });

        _logger.LogInformation("Disabled TOTP for user {UserId}", userId);
    }

    public async Task<List<RecoveryCodeRecord>> GetRecoveryCodesAsync(string userId)
    {
        await using var connection = new SqliteConnection(_connectionString);

        const string sql = """
            SELECT id AS Id, user_id AS UserId, code_hash AS CodeHash, used_at AS UsedAt
            FROM recovery_codes
            WHERE user_id = @UserId AND used_at IS NULL;
            """;

        var codes = await connection.QueryAsync<RecoveryCodeRecord>(sql, new { UserId = userId });
        return codes.ToList();
    }

    public async Task AddRecoveryCodesAsync(string userId, List<string> codeHashes)
    {
        await using var connection = new SqliteConnection(_connectionString);

        const string sql = """
            INSERT INTO recovery_codes (user_id, code_hash)
            VALUES (@UserId, @CodeHash);
            """;

        foreach (var codeHash in codeHashes)
        {
            await connection.ExecuteAsync(sql, new { UserId = userId, CodeHash = codeHash });
        }

        _logger.LogInformation("Added {Count} recovery codes for user {UserId}", codeHashes.Count, userId);
    }

    public async Task<bool> UseRecoveryCodeAsync(string userId, string codeHash)
    {
        await using var connection = new SqliteConnection(_connectionString);

        const string sql = """
            UPDATE recovery_codes
            SET used_at = @Now
            WHERE user_id = @UserId AND code_hash = @CodeHash AND used_at IS NULL;
            """;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var affected = await connection.ExecuteAsync(sql, new { UserId = userId, CodeHash = codeHash, Now = now });

        return affected > 0;
    }
}
