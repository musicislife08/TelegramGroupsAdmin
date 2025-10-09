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

public class InviteRepository
{
    private readonly string _connectionString;
    private readonly ILogger<InviteRepository> _logger;

    public InviteRepository(IConfiguration configuration, ILogger<InviteRepository> logger)
    {
        _connectionString = configuration.GetConnectionString("PostgreSQL")
            ?? throw new InvalidOperationException("PostgreSQL connection string not found");
        _logger = logger;
    }

    public async Task<UiModels.InviteRecord?> GetByTokenAsync(string token, CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        const string sql = """
            SELECT token, created_by, created_at, expires_at, used_by,
                   permission_level, status, modified_at
            FROM invites
            WHERE token = @Token;
            """;

        var dto = await connection.QueryFirstOrDefaultAsync<DataModels.InviteRecordDto>(sql, new { Token = token });
        return dto?.ToInviteRecord().ToUiModel();
    }

    public async Task CreateAsync(UiModels.InviteRecord invite, CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        const string sql = """
            INSERT INTO invites (token, created_by, created_at, expires_at, permission_level)
            VALUES (@Token, @CreatedBy, @CreatedAt, @ExpiresAt, @PermissionLevel);
            """;

        await connection.ExecuteAsync(sql, new
        {
            invite.Token,
            invite.CreatedBy,
            invite.CreatedAt,
            invite.ExpiresAt,
            invite.PermissionLevel
        });

        _logger.LogInformation("Created invite {Token} by user {CreatedBy}, expires at {ExpiresAt}, permission level {PermissionLevel}",
            invite.Token, invite.CreatedBy, DateTimeOffset.FromUnixTimeSeconds(invite.ExpiresAt), invite.PermissionLevel);
    }

    public async Task<string> CreateAsync(string createdBy, int validDays = 7, int permissionLevel = 0, CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        var token = Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var expiresAt = now + (validDays * 24 * 3600);

        const string sql = """
            INSERT INTO invites (token, created_by, created_at, expires_at, permission_level)
            VALUES (@Token, @CreatedBy, @CreatedAt, @ExpiresAt, @PermissionLevel);
            """;

        await connection.ExecuteAsync(sql, new
        {
            Token = token,
            CreatedBy = createdBy,
            CreatedAt = now,
            ExpiresAt = expiresAt,
            PermissionLevel = permissionLevel
        });

        var permissionName = permissionLevel switch
        {
            0 => "ReadOnly",
            1 => "Admin",
            2 => "Owner",
            _ => permissionLevel.ToString()
        };

        _logger.LogInformation("Created invite {Token} by user {CreatedBy}, expires at {ExpiresAt}, permission level {PermissionLevel}",
            token, createdBy, DateTimeOffset.FromUnixTimeSeconds(expiresAt), permissionName);

        return token;
    }

    public async Task MarkAsUsedAsync(string token, string usedBy)
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        const string sql = """
            UPDATE invites
            SET used_by = @UsedBy, status = 1, modified_at = @Now
            WHERE token = @Token;
            """;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await connection.ExecuteAsync(sql, new { Token = token, UsedBy = usedBy, Now = now });

        _logger.LogInformation("Invite {Token} used by user {UsedBy}", token, usedBy);
    }

    public async Task<List<UiModels.InviteRecord>> GetByCreatorAsync(string createdBy, CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        const string sql = """
            SELECT token, created_by, created_at, expires_at, used_by,
                   permission_level, status, modified_at
            FROM invites
            WHERE created_by = @CreatedBy
            ORDER BY created_at DESC;
            """;

        var dtos = await connection.QueryAsync<DataModels.InviteRecordDto>(sql, new { CreatedBy = createdBy });
        return dtos.Select(dto => dto.ToInviteRecord().ToUiModel()).ToList();
    }

    public async Task<int> CleanupExpiredAsync()
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        const string sql = """
            DELETE FROM invites
            WHERE expires_at <= @Now AND status = 0;
            """;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var deleted = await connection.ExecuteAsync(sql, new { Now = now });

        if (deleted > 0)
        {
            _logger.LogInformation("Cleaned up {Count} expired invites", deleted);
        }

        return deleted;
    }

    public async Task<List<UiModels.InviteRecord>> GetAllAsync(DataModels.InviteFilter filter = DataModels.InviteFilter.Pending, CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        const string baseSql = """
            SELECT token, created_by, created_at, expires_at, used_by,
                   permission_level, status, modified_at
            FROM invites
            """;

        const string allSql = baseSql + " ORDER BY created_at DESC;";
        const string filteredSql = baseSql + " WHERE status = @Status ORDER BY created_at DESC;";

        if (filter == DataModels.InviteFilter.All)
        {
            var dtos = await connection.QueryAsync<DataModels.InviteRecordDto>(allSql);
            return dtos.Select(dto => dto.ToInviteRecord().ToUiModel()).ToList();
        }
        else
        {
            var dtos = await connection.QueryAsync<DataModels.InviteRecordDto>(filteredSql, new { Status = (int)filter });
            return dtos.Select(dto => dto.ToInviteRecord().ToUiModel()).ToList();
        }
    }

    public async Task<List<UiModels.InviteWithCreator>> GetAllWithCreatorEmailAsync(DataModels.InviteFilter filter = DataModels.InviteFilter.Pending, CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        const string baseSql = """
            SELECT i.token, i.created_by, i.created_at, i.expires_at, i.used_by,
                   i.permission_level, i.status, i.modified_at,
                   u.email as creator_email
            FROM invites i
            LEFT JOIN users u ON i.created_by = u.id
            """;

        const string allSql = baseSql + " ORDER BY i.created_at DESC;";
        const string filteredSql = baseSql + " WHERE i.status = @Status ORDER BY i.created_at DESC;";

        if (filter == DataModels.InviteFilter.All)
        {
            var results = await connection.QueryAsync<DataModels.InviteWithCreatorDto>(allSql);
            return results.Select(dto => dto.ToInviteWithCreator().ToUiModel()).ToList();
        }
        else
        {
            var results = await connection.QueryAsync<DataModels.InviteWithCreatorDto>(filteredSql, new { Status = (int)filter });
            return results.Select(dto => dto.ToInviteWithCreator().ToUiModel()).ToList();
        }
    }

    public async Task<bool> RevokeAsync(string token, CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        const string sql = """
            UPDATE invites
            SET status = 2, modified_at = @ModifiedAt
            WHERE token = @Token AND status = 0;
            """;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var updated = await connection.ExecuteAsync(sql, new { Token = token, ModifiedAt = now });

        if (updated > 0)
        {
            _logger.LogInformation("Revoked invite {Token}", token);
            return true;
        }

        return false;
    }
}
