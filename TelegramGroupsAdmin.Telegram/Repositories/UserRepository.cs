using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data;
using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories;

public class UserRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<UserRepository> _logger;

    public UserRepository(IDbContextFactory<AppDbContext> contextFactory, ILogger<UserRepository> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task<int> GetUserCountAsync(CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        return await context.Users.CountAsync(ct);
    }

    public async Task<UiModels.UserRecord?> GetByEmailAsync(string email, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var normalizedEmail = email.ToUpperInvariant();

        var entity = await context.Users
            .AsNoTracking()
            .Where(u => u.NormalizedEmail == normalizedEmail && u.Status != DataModels.UserStatus.Deleted)
            .FirstOrDefaultAsync(ct);

        return entity?.ToModel();
    }

    public async Task<UiModels.UserRecord?> GetByEmailIncludingDeletedAsync(string email, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var normalizedEmail = email.ToUpperInvariant();

        var entity = await context.Users
            .AsNoTracking()
            .Where(u => u.NormalizedEmail == normalizedEmail)
            .FirstOrDefaultAsync(ct);

        return entity?.ToModel();
    }

    public async Task<UiModels.UserRecord?> GetByIdAsync(string userId, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var entity = await context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        return entity?.ToModel();
    }

    public async Task<string> CreateAsync(UiModels.UserRecord user, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var entity = user.ToDto();
        entity.NormalizedEmail = user.Email.ToUpperInvariant();

        context.Users.Add(entity);
        await context.SaveChangesAsync(ct);

        _logger.LogInformation("Created user {Email} with ID {UserId}", user.Email, user.Id);

        return user.Id;
    }

    public async Task UpdateLastLoginAsync(string userId, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var entity = await context.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (entity == null) return;

        entity.LastLoginAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(ct);
    }

    public async Task UpdateSecurityStampAsync(string userId, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var entity = await context.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (entity == null) return;

        entity.SecurityStamp = Guid.NewGuid().ToString();
        await context.SaveChangesAsync(ct);
    }

    public async Task UpdateTotpSecretAsync(string userId, string totpSecret, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var entity = await context.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (entity == null) return;

        entity.TotpSecret = totpSecret;
        entity.TotpSetupStartedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(ct);
    }

    public async Task EnableTotpAsync(string userId, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var entity = await context.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (entity == null) return;

        entity.TotpEnabled = true;
        entity.TotpSetupStartedAt = null;
        await context.SaveChangesAsync(ct);

        _logger.LogInformation("Enabled TOTP for user {UserId}", userId);
    }

    public async Task DisableTotpAsync(string userId, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var entity = await context.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (entity == null) return;

        entity.TotpSecret = null;
        entity.TotpEnabled = false;
        await context.SaveChangesAsync(ct);

        _logger.LogInformation("Disabled TOTP for user {UserId}", userId);
    }

    public async Task ResetTotpAsync(string userId, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var entity = await context.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (entity == null) return;

        entity.TotpSecret = null;
        entity.TotpEnabled = false;
        entity.TotpSetupStartedAt = null;
        await context.SaveChangesAsync(ct);

        _logger.LogInformation("Reset TOTP for user {UserId}", userId);
    }

    public async Task DeleteRecoveryCodesAsync(string userId, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var codes = await context.RecoveryCodes
            .Where(rc => rc.UserId == userId)
            .ToListAsync(ct);

        context.RecoveryCodes.RemoveRange(codes);
        await context.SaveChangesAsync(ct);

        _logger.LogInformation("Deleted all recovery codes for user {UserId}", userId);
    }

    public async Task<List<UiModels.RecoveryCodeRecord>> GetRecoveryCodesAsync(string userId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var entities = await context.RecoveryCodes
            .AsNoTracking()
            .Where(rc => rc.UserId == userId && rc.UsedAt == null)
            .ToListAsync();

        return entities.Select(e => e.ToModel()).ToList();
    }

    public async Task AddRecoveryCodesAsync(string userId, List<string> codeHashes)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var entities = codeHashes.Select(codeHash => new DataModels.RecoveryCodeRecordDto
        {
            UserId = userId,
            CodeHash = codeHash,
            UsedAt = null
        }).ToList();

        context.RecoveryCodes.AddRange(entities);
        await context.SaveChangesAsync();

        _logger.LogInformation("Added {Count} recovery codes for user {UserId}", codeHashes.Count, userId);
    }

    public async Task CreateRecoveryCodeAsync(string userId, string codeHash, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var entity = new DataModels.RecoveryCodeRecordDto
        {
            UserId = userId,
            CodeHash = codeHash,
            UsedAt = null
        };

        context.RecoveryCodes.Add(entity);
        await context.SaveChangesAsync(ct);
    }

    public async Task<bool> UseRecoveryCodeAsync(string userId, string codeHash, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var entity = await context.RecoveryCodes
            .FirstOrDefaultAsync(rc => rc.UserId == userId && rc.CodeHash == codeHash && rc.UsedAt == null, ct);

        if (entity == null)
            return false;

        entity.UsedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(ct);

        return true;
    }

    public async Task<UiModels.InviteRecord?> GetInviteByTokenAsync(string token, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var entity = await context.Invites
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Token == token, ct);

        return entity?.ToModel();
    }

    public async Task UseInviteAsync(string token, string userId, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var entity = await context.Invites.FirstOrDefaultAsync(i => i.Token == token, ct);
        if (entity == null) return;

        entity.UsedBy = userId;
        entity.Status = DataModels.InviteStatus.Used;
        entity.ModifiedAt = DateTimeOffset.UtcNow;

        await context.SaveChangesAsync(ct);

        _logger.LogInformation("Invite {Token} used by user {UserId}", token, userId);
    }

    public async Task<List<UiModels.UserRecord>> GetAllAsync(CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var entities = await context.Users
            .AsNoTracking()
            .OrderByDescending(u => u.CreatedAt)
            .ToListAsync(ct);

        return entities.Select(e => e.ToModel()).ToList();
    }

    public async Task<List<UiModels.UserRecord>> GetAllIncludingDeletedAsync(CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var entities = await context.Users
            .AsNoTracking()
            .OrderByDescending(u => u.CreatedAt)
            .ToListAsync(ct);

        return entities.Select(e => e.ToModel()).ToList();
    }

    public async Task UpdatePermissionLevelAsync(string userId, int permissionLevel, string modifiedBy, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var entity = await context.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (entity == null) return;

        entity.PermissionLevel = (DataModels.PermissionLevel)permissionLevel;
        entity.ModifiedBy = modifiedBy;
        entity.ModifiedAt = DateTimeOffset.UtcNow;

        await context.SaveChangesAsync(ct);

        _logger.LogInformation("Updated permission level for user {UserId} to {PermissionLevel} by {ModifiedBy}", userId, permissionLevel, modifiedBy);
    }

    public async Task SetActiveAsync(string userId, bool isActive, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var entity = await context.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (entity == null) return;

        entity.IsActive = isActive;
        await context.SaveChangesAsync(ct);

        _logger.LogInformation("Set user {UserId} active status to {IsActive}", userId, isActive);
    }

    public async Task UpdateStatusAsync(string userId, UiModels.UserStatus newStatus, string modifiedBy, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var entity = await context.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (entity == null) return;

        entity.Status = (DataModels.UserStatus)(int)newStatus;
        entity.IsActive = newStatus == UiModels.UserStatus.Active;
        entity.ModifiedBy = modifiedBy;
        entity.ModifiedAt = DateTimeOffset.UtcNow;

        await context.SaveChangesAsync(ct);

        _logger.LogInformation("Updated status for user {UserId} to {Status} by {ModifiedBy}", userId, newStatus, modifiedBy);
    }

    public async Task UpdateAsync(UiModels.UserRecord user, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var entity = await context.Users.FirstOrDefaultAsync(u => u.Id == user.Id, ct);
        if (entity == null) return;

        // Update all fields
        entity.Email = user.Email;
        entity.NormalizedEmail = user.NormalizedEmail;
        entity.PasswordHash = user.PasswordHash;
        entity.SecurityStamp = user.SecurityStamp;
        entity.PermissionLevel = (DataModels.PermissionLevel)(int)user.PermissionLevel;
        entity.InvitedBy = user.InvitedBy;
        entity.IsActive = user.IsActive;
        entity.TotpSecret = user.TotpSecret;
        entity.TotpEnabled = user.TotpEnabled;
        entity.LastLoginAt = user.LastLoginAt;
        entity.Status = (DataModels.UserStatus)(int)user.Status;
        entity.ModifiedBy = user.ModifiedBy;
        entity.ModifiedAt = user.ModifiedAt;
        entity.EmailVerified = user.EmailVerified;
        entity.EmailVerificationToken = user.EmailVerificationToken;
        entity.EmailVerificationTokenExpiresAt = user.EmailVerificationTokenExpiresAt;
        entity.PasswordResetToken = user.PasswordResetToken;
        entity.PasswordResetTokenExpiresAt = user.PasswordResetTokenExpiresAt;

        await context.SaveChangesAsync(ct);

        _logger.LogInformation("Updated user {UserId}", user.Id);
    }

}
