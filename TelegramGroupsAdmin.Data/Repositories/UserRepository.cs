using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Data.Repositories;

// CRITICAL DAPPER/DTO CONVENTION:
// All SQL SELECT statements MUST use raw snake_case column names without aliases.
// DTOs use positional record constructors that are CASE-SENSITIVE.
//
// ✅ CORRECT:   SELECT user_id, email FROM users
// ❌ INCORRECT: SELECT user_id AS UserId, email AS Email FROM users
//
// See MessageRecord.cs for detailed explanation.

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

    public async Task<int> GetUserCountAsync(CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);

        const string sql = "SELECT COUNT(*) FROM users;";

        return await connection.ExecuteScalarAsync<int>(sql);
    }

    public async Task<UserRecord?> GetByEmailAsync(string email, CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        var normalizedEmail = email.ToUpperInvariant();

        const string sql = """
            SELECT id, email, normalized_email, password_hash, security_stamp,
                   permission_level, invited_by, is_active, totp_secret,
                   totp_enabled, created_at, last_login_at, status,
                   modified_by, modified_at, email_verified, email_verification_token,
                   email_verification_token_expires_at, password_reset_token,
                   password_reset_token_expires_at, totp_setup_started_at
            FROM users
            WHERE normalized_email = @NormalizedEmail AND status = 1;
            """;

        var dto = await connection.QueryFirstOrDefaultAsync<UserRecordDto>(sql, new { NormalizedEmail = normalizedEmail });
        return dto?.ToUserRecord();
    }

    public async Task<UserRecord?> GetByEmailIncludingDeletedAsync(string email, CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        var normalizedEmail = email.ToUpperInvariant();

        const string sql = """
            SELECT id, email, normalized_email, password_hash, security_stamp,
                   permission_level, invited_by, is_active, totp_secret,
                   totp_enabled, created_at, last_login_at, status,
                   modified_by, modified_at, email_verified, email_verification_token,
                   email_verification_token_expires_at, password_reset_token,
                   password_reset_token_expires_at, totp_setup_started_at
            FROM users
            WHERE normalized_email = @NormalizedEmail;
            """;

        var dto = await connection.QueryFirstOrDefaultAsync<UserRecordDto>(sql, new { NormalizedEmail = normalizedEmail });
        return dto?.ToUserRecord();
    }

    public async Task<UserRecord?> GetByIdAsync(string userId, CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);

        const string sql = """
            SELECT id, email, normalized_email, password_hash, security_stamp,
                   permission_level, invited_by, is_active, totp_secret,
                   totp_enabled, created_at, last_login_at, status,
                   modified_by, modified_at, email_verified, email_verification_token,
                   email_verification_token_expires_at, password_reset_token,
                   password_reset_token_expires_at, totp_setup_started_at
            FROM users
            WHERE id = @UserId;
            """;

        var dto = await connection.QueryFirstOrDefaultAsync<UserRecordDto>(sql, new { UserId = userId });
        return dto?.ToUserRecord();
    }

    public async Task<string> CreateAsync(UserRecord user, CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);

        var normalizedEmail = user.Email.ToUpperInvariant();

        const string sql = """
            INSERT INTO users (
                id, email, normalized_email, password_hash, security_stamp,
                permission_level, invited_by, is_active, totp_secret, totp_enabled,
                created_at, last_login_at, email_verified, email_verification_token,
                email_verification_token_expires_at, password_reset_token,
                password_reset_token_expires_at
            ) VALUES (
                @Id, @Email, @NormalizedEmail, @PasswordHash, @SecurityStamp,
                @PermissionLevel, @InvitedBy, @IsActive, @TotpSecret, @TotpEnabled,
                @CreatedAt, @LastLoginAt, @EmailVerified, @EmailVerificationToken,
                @EmailVerificationTokenExpiresAt, @PasswordResetToken,
                @PasswordResetTokenExpiresAt
            );
            """;

        await connection.ExecuteAsync(sql, new
        {
            user.Id,
            user.Email,
            NormalizedEmail = normalizedEmail,
            user.PasswordHash,
            user.SecurityStamp,
            user.PermissionLevel,
            user.InvitedBy,
            user.IsActive,
            user.TotpSecret,
            user.TotpEnabled,
            user.CreatedAt,
            user.LastLoginAt,
            user.EmailVerified,
            user.EmailVerificationToken,
            user.EmailVerificationTokenExpiresAt,
            user.PasswordResetToken,
            user.PasswordResetTokenExpiresAt
        });

        _logger.LogInformation("Created user {Email} with ID {UserId}", user.Email, user.Id);

        return user.Id;
    }

    public async Task UpdateLastLoginAsync(string userId, CancellationToken ct = default)
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

    public async Task UpdateSecurityStampAsync(string userId, CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);

        const string sql = """
            UPDATE users
            SET security_stamp = @SecurityStamp
            WHERE id = @UserId;
            """;

        var newStamp = Guid.NewGuid().ToString();
        await connection.ExecuteAsync(sql, new { UserId = userId, SecurityStamp = newStamp });
    }

    public async Task UpdateTotpSecretAsync(string userId, string totpSecret, CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);

        const string sql = """
            UPDATE users
            SET totp_secret = @TotpSecret,
                totp_setup_started_at = @TotpSetupStartedAt
            WHERE id = @UserId;
            """;

        await connection.ExecuteAsync(sql, new
        {
            UserId = userId,
            TotpSecret = totpSecret,
            TotpSetupStartedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });
    }

    public async Task EnableTotpAsync(string userId, CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);

        const string sql = """
            UPDATE users
            SET totp_enabled = 1,
                totp_setup_started_at = NULL
            WHERE id = @UserId;
            """;

        await connection.ExecuteAsync(sql, new { UserId = userId });

        _logger.LogInformation("Enabled TOTP for user {UserId}", userId);
    }

    public async Task DisableTotpAsync(string userId, CancellationToken ct = default)
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

    public async Task ResetTotpAsync(string userId, CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);

        // Clear TOTP secret, disable TOTP, and clear setup timestamp
        const string sql = """
            UPDATE users
            SET totp_secret = NULL,
                totp_enabled = 0,
                totp_setup_started_at = NULL
            WHERE id = @UserId;
            """;

        await connection.ExecuteAsync(sql, new { UserId = userId });

        _logger.LogInformation("Reset TOTP for user {UserId}", userId);
    }

    public async Task DeleteRecoveryCodesAsync(string userId, CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);

        const string sql = """
            DELETE FROM recovery_codes
            WHERE user_id = @UserId;
            """;

        await connection.ExecuteAsync(sql, new { UserId = userId });

        _logger.LogInformation("Deleted all recovery codes for user {UserId}", userId);
    }

    public async Task<List<RecoveryCodeRecord>> GetRecoveryCodesAsync(string userId)
    {
        await using var connection = new SqliteConnection(_connectionString);

        const string sql = """
            SELECT id, user_id, code_hash, used_at
            FROM recovery_codes
            WHERE user_id = @UserId AND used_at IS NULL;
            """;

        var dtos = await connection.QueryAsync<RecoveryCodeRecordDto>(sql, new { UserId = userId });
        return dtos.Select(dto => dto.ToRecoveryCodeRecord()).ToList();
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

    public async Task CreateRecoveryCodeAsync(string userId, string codeHash, CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);

        const string sql = """
            INSERT INTO recovery_codes (user_id, code_hash)
            VALUES (@UserId, @CodeHash);
            """;

        await connection.ExecuteAsync(sql, new { UserId = userId, CodeHash = codeHash });
    }

    public async Task<bool> UseRecoveryCodeAsync(string userId, string codeHash, CancellationToken ct = default)
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

    public async Task<InviteRecord?> GetInviteByTokenAsync(string token, CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);

        const string sql = """
            SELECT token, created_by, created_at, expires_at, used_by,
                   permission_level, status, modified_at
            FROM invites
            WHERE token = @Token;
            """;

        var dto = await connection.QueryFirstOrDefaultAsync<InviteRecordDto>(sql, new { Token = token });
        return dto?.ToInviteRecord();
    }

    public async Task UseInviteAsync(string token, string userId, CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);

        const string sql = """
            UPDATE invites
            SET used_by = @UserId, status = 1, modified_at = @Now
            WHERE token = @Token;
            """;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await connection.ExecuteAsync(sql, new { Token = token, UserId = userId, Now = now });

        _logger.LogInformation("Invite {Token} used by user {UserId}", token, userId);
    }

    public async Task<List<UserRecord>> GetAllAsync(CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);

        const string sql = """
            SELECT id, email, normalized_email, password_hash, security_stamp,
                   permission_level, invited_by, is_active, totp_secret,
                   totp_enabled, created_at, last_login_at, status,
                   modified_by, modified_at, email_verified, email_verification_token,
                   email_verification_token_expires_at, password_reset_token,
                   password_reset_token_expires_at, totp_setup_started_at
            FROM users
            WHERE status != 3
            ORDER BY created_at DESC;
            """;

        var dtos = await connection.QueryAsync<UserRecordDto>(sql);
        return dtos.Select(dto => dto.ToUserRecord()).ToList();
    }

    public async Task<List<UserRecord>> GetAllIncludingDeletedAsync(CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);

        const string sql = """
            SELECT id, email, normalized_email, password_hash, security_stamp,
                   permission_level, invited_by, is_active, totp_secret,
                   totp_enabled, created_at, last_login_at, status,
                   modified_by, modified_at, email_verified, email_verification_token,
                   email_verification_token_expires_at, password_reset_token,
                   password_reset_token_expires_at, totp_setup_started_at
            FROM users
            ORDER BY created_at DESC;
            """;

        var dtos = await connection.QueryAsync<UserRecordDto>(sql);
        return dtos.Select(dto => dto.ToUserRecord()).ToList();
    }

    public async Task UpdatePermissionLevelAsync(string userId, int permissionLevel, string modifiedBy, CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);

        const string sql = """
            UPDATE users
            SET permission_level = @PermissionLevel,
                modified_by = @ModifiedBy,
                modified_at = @ModifiedAt
            WHERE id = @UserId;
            """;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await connection.ExecuteAsync(sql, new
        {
            UserId = userId,
            PermissionLevel = permissionLevel,
            ModifiedBy = modifiedBy,
            ModifiedAt = now
        });

        _logger.LogInformation("Updated permission level for user {UserId} to {PermissionLevel} by {ModifiedBy}", userId, permissionLevel, modifiedBy);
    }

    public async Task SetActiveAsync(string userId, bool isActive, CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);

        const string sql = """
            UPDATE users
            SET is_active = @IsActive
            WHERE id = @UserId;
            """;

        await connection.ExecuteAsync(sql, new { UserId = userId, IsActive = isActive });

        _logger.LogInformation("Set user {UserId} active status to {IsActive}", userId, isActive);
    }

    public async Task UpdateStatusAsync(string userId, UserStatus newStatus, string modifiedBy, CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);

        const string sql = """
            UPDATE users
            SET status = @Status,
                is_active = @IsActive,
                modified_by = @ModifiedBy,
                modified_at = @ModifiedAt
            WHERE id = @UserId;
            """;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await connection.ExecuteAsync(sql, new
        {
            UserId = userId,
            Status = (int)newStatus,
            IsActive = newStatus == UserStatus.Active, // Keep is_active in sync for backward compatibility
            ModifiedBy = modifiedBy,
            ModifiedAt = now
        });

        _logger.LogInformation("Updated status for user {UserId} to {Status} by {ModifiedBy}", userId, newStatus, modifiedBy);
    }

    public async Task UpdateAsync(UserRecord user, CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);

        const string sql = """
            UPDATE users
            SET email = @Email,
                normalized_email = @NormalizedEmail,
                password_hash = @PasswordHash,
                security_stamp = @SecurityStamp,
                permission_level = @PermissionLevel,
                invited_by = @InvitedBy,
                is_active = @IsActive,
                totp_secret = @TotpSecret,
                totp_enabled = @TotpEnabled,
                last_login_at = @LastLoginAt,
                status = @Status,
                modified_by = @ModifiedBy,
                modified_at = @ModifiedAt,
                email_verified = @EmailVerified,
                email_verification_token = @EmailVerificationToken,
                email_verification_token_expires_at = @EmailVerificationTokenExpiresAt,
                password_reset_token = @PasswordResetToken,
                password_reset_token_expires_at = @PasswordResetTokenExpiresAt
            WHERE id = @Id;
            """;

        await connection.ExecuteAsync(sql, new
        {
            user.Id,
            user.Email,
            user.NormalizedEmail,
            user.PasswordHash,
            user.SecurityStamp,
            user.PermissionLevel,
            user.InvitedBy,
            IsActive = user.IsActive ? 1 : 0,
            user.TotpSecret,
            TotpEnabled = user.TotpEnabled ? 1 : 0,
            user.LastLoginAt,
            Status = (int)user.Status,
            user.ModifiedBy,
            user.ModifiedAt,
            EmailVerified = user.EmailVerified ? 1 : 0,
            user.EmailVerificationToken,
            user.EmailVerificationTokenExpiresAt,
            user.PasswordResetToken,
            user.PasswordResetTokenExpiresAt
        });

        _logger.LogInformation("Updated user {UserId}", user.Id);
    }

}
