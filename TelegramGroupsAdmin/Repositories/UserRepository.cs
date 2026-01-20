using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Core.Mappings;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Data;
using DataModels = TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Repositories;

public class UserRepository : IUserRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<UserRepository> _logger;

    public UserRepository(IDbContextFactory<AppDbContext> contextFactory, ILogger<UserRepository> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task<int> GetUserCountAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Users.CountAsync(cancellationToken);
    }

    public async Task<UserRecord?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var normalizedEmail = email.ToUpperInvariant();

        var entity = await context.Users
            .AsNoTracking()
            .Where(u => u.NormalizedEmail == normalizedEmail && u.Status != DataModels.UserStatus.Deleted)
            .FirstOrDefaultAsync(cancellationToken);

        return entity?.ToModel();
    }

    public async Task<UserRecord?> GetByEmailIncludingDeletedAsync(string email, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var normalizedEmail = email.ToUpperInvariant();

        var entity = await context.Users
            .AsNoTracking()
            .Where(u => u.NormalizedEmail == normalizedEmail)
            .FirstOrDefaultAsync(cancellationToken);

        return entity?.ToModel();
    }

    public async Task<UserRecord?> GetByIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        return entity?.ToModel();
    }

    /// <inheritdoc/>
    public async Task<List<UserRecord>> GetByIdsAsync(
        IEnumerable<string> userIds,
        CancellationToken cancellationToken = default)
    {
        var idList = userIds.ToList();
        if (idList.Count == 0)
            return [];

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entities = await context.Users
            .AsNoTracking()
            .Where(u => idList.Contains(u.Id))
            .ToListAsync(cancellationToken);

        return entities.Select(e => e.ToModel()).ToList();
    }

    public async Task<string> CreateAsync(UserRecord user, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = user.ToDto();
        entity.NormalizedEmail = user.Email.ToUpperInvariant();

        context.Users.Add(entity);
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created user {User}", user.ToLogInfo());

        return user.Id;
    }

    /// <summary>
    /// Atomically register a user (create or reactivate) and mark the invite as used in a single transaction.
    /// - If email doesn't exist: creates new user with generated ID
    /// - If email exists (deleted user): reactivates with fresh credentials, resets TOTP/verification
    /// </summary>
    public async Task<string> RegisterUserWithInviteAsync(
        string email,
        string passwordHash,
        PermissionLevel permissionLevel,
        string? invitedBy,
        string inviteToken,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var normalizedEmail = email.ToUpperInvariant();
        var now = DateTimeOffset.UtcNow;

        // Step 1: Validate invite exists FIRST (fail-fast before modifying user)
        var inviteEntity = await context.Invites.FirstOrDefaultAsync(i => i.Token == inviteToken, cancellationToken)
            ?? throw new InvalidOperationException(
                "Invite token not found after validation - possible race condition or data integrity issue");

        // Step 2: Check if user exists by email (including deleted users)
        var existingEntity = await context.Users
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken);

        string userId;
        bool isReactivation = existingEntity != null;

        // Step 3: Create or reactivate user
        if (isReactivation)
        {
            // Reactivate existing (deleted) user with fresh credentials
            userId = existingEntity!.Id;
            existingEntity.Email = email;
            existingEntity.NormalizedEmail = normalizedEmail;
            existingEntity.PasswordHash = passwordHash;
            existingEntity.SecurityStamp = Guid.NewGuid().ToString();
            existingEntity.PermissionLevel = (DataModels.PermissionLevel)(int)permissionLevel;
            existingEntity.InvitedBy = invitedBy;
            existingEntity.Status = DataModels.UserStatus.Active;
            existingEntity.IsActive = true;
            // Reset security-sensitive fields - require 2FA setup
            existingEntity.TotpSecret = null;
            existingEntity.TotpEnabled = true; // All users must set up 2FA by default
            existingEntity.TotpSetupStartedAt = null;
            existingEntity.EmailVerified = false;
            existingEntity.EmailVerificationToken = null;
            existingEntity.EmailVerificationTokenExpiresAt = null;
            existingEntity.PasswordResetToken = null;
            existingEntity.PasswordResetTokenExpiresAt = null;
            existingEntity.FailedLoginAttempts = 0;
            existingEntity.LockedUntil = null;
            existingEntity.ModifiedBy = existingEntity.Id;
            existingEntity.ModifiedAt = now;
        }
        else
        {
            // Create new user with generated ID
            userId = Guid.NewGuid().ToString();
            var newUser = new DataModels.UserRecordDto
            {
                Id = userId,
                Email = email,
                NormalizedEmail = normalizedEmail,
                PasswordHash = passwordHash,
                SecurityStamp = Guid.NewGuid().ToString(),
                PermissionLevel = (DataModels.PermissionLevel)(int)permissionLevel,
                InvitedBy = invitedBy,
                Status = DataModels.UserStatus.Active,
                IsActive = true,
                TotpSecret = null,
                TotpEnabled = true, // All users must set up 2FA by default
                TotpSetupStartedAt = null,
                CreatedAt = now,
                LastLoginAt = null,
                EmailVerified = false,
                EmailVerificationToken = null,
                EmailVerificationTokenExpiresAt = null,
                PasswordResetToken = null,
                PasswordResetTokenExpiresAt = null,
                FailedLoginAttempts = 0,
                LockedUntil = null,
                ModifiedBy = null,
                ModifiedAt = null
            };
            context.Users.Add(newUser);
        }

        // Step 4: Mark invite as used (already loaded and validated in Step 1)
        inviteEntity.UsedBy = userId;
        inviteEntity.Status = DataModels.InviteStatus.Used;
        inviteEntity.ModifiedAt = now;

        // Single SaveChangesAsync for both operations (implicit transaction)
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Atomically {Action} user {Email} with ID {UserId} and marked invite {Token} as used",
            isReactivation ? "reactivated" : "created",
            email, userId, inviteToken);

        return userId;
    }

    public async Task UpdateLastLoginAsync(string userId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (entity == null) return;

        entity.LastLoginAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateSecurityStampAsync(string userId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (entity == null) return;

        entity.SecurityStamp = Guid.NewGuid().ToString();
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateTotpSecretAsync(string userId, string totpSecret, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (entity == null) return;

        entity.TotpSecret = totpSecret;
        entity.TotpSetupStartedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task EnableTotpAsync(string userId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (entity == null) return;

        entity.TotpEnabled = true;
        entity.TotpSetupStartedAt = null;
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Enabled TOTP for {User}", LogDisplayName.WebUserInfo(entity.Email, userId));
    }

    public async Task DisableTotpAsync(string userId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (entity == null) return;

        // IMPORTANT: Only set TotpEnabled=false, KEEP the secret and timestamp
        // This allows user to re-enable TOTP later without re-scanning QR code
        entity.TotpEnabled = false;
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Disabled TOTP for {User} (secret preserved)", LogDisplayName.WebUserInfo(entity.Email, userId));
    }

    public async Task ResetTotpAsync(string userId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (entity == null) return;

        entity.TotpSecret = null;
        entity.TotpEnabled = false;
        entity.TotpSetupStartedAt = null;
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Reset TOTP for {User}", LogDisplayName.WebUserInfo(entity.Email, userId));
    }

    public async Task DeleteRecoveryCodesAsync(string userId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var codes = await context.RecoveryCodes
            .Where(rc => rc.UserId == userId)
            .ToListAsync(cancellationToken);

        context.RecoveryCodes.RemoveRange(codes);
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Deleted all recovery codes for user {UserId}", userId);
    }

    public async Task<List<RecoveryCodeRecord>> GetRecoveryCodesAsync(string userId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entities = await context.RecoveryCodes
            .AsNoTracking()
            .Where(rc => rc.UserId == userId && rc.UsedAt == null)
            .ToListAsync(cancellationToken);

        return entities.Select(e => e.ToModel()).ToList();
    }

    public async Task AddRecoveryCodesAsync(string userId, List<string> codeHashes, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entities = codeHashes.Select(codeHash => new DataModels.RecoveryCodeRecordDto
        {
            UserId = userId,
            CodeHash = codeHash,
            UsedAt = null
        }).ToList();

        context.RecoveryCodes.AddRange(entities);
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Added {Count} recovery codes for user {UserId}", codeHashes.Count, userId);
    }

    public async Task CreateRecoveryCodeAsync(string userId, string codeHash, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = new DataModels.RecoveryCodeRecordDto
        {
            UserId = userId,
            CodeHash = codeHash,
            UsedAt = null
        };

        context.RecoveryCodes.Add(entity);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> UseRecoveryCodeAsync(string userId, string codeHash, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.RecoveryCodes
            .FirstOrDefaultAsync(rc => rc.UserId == userId && rc.CodeHash == codeHash && rc.UsedAt == null, cancellationToken);

        if (entity == null)
            return false;

        entity.UsedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task<InviteRecord?> GetInviteByTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.Invites
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Token == token, cancellationToken);

        return entity?.ToModel();
    }

    public async Task UseInviteAsync(string token, string userId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.Invites.FirstOrDefaultAsync(i => i.Token == token, cancellationToken);
        if (entity == null) return;

        entity.UsedBy = userId;
        entity.Status = DataModels.InviteStatus.Used;
        entity.ModifiedAt = DateTimeOffset.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Invite {Token} used by user {UserId}", token, userId);
    }

    public async Task<List<UserRecord>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entities = await context.Users
            .AsNoTracking()
            .OrderByDescending(u => u.CreatedAt)
            .ToListAsync(cancellationToken);

        return entities.Select(e => e.ToModel()).ToList();
    }

    public async Task<List<UserRecord>> GetAllIncludingDeletedAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entities = await context.Users
            .AsNoTracking()
            .OrderByDescending(u => u.CreatedAt)
            .ToListAsync(cancellationToken);

        return entities.Select(e => e.ToModel()).ToList();
    }

    public async Task UpdatePermissionLevelAsync(string userId, int permissionLevel, string modifiedBy, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (entity == null) return;

        entity.PermissionLevel = (DataModels.PermissionLevel)permissionLevel;
        entity.ModifiedBy = modifiedBy;
        entity.ModifiedAt = DateTimeOffset.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Updated permission level for {User} to {PermissionLevel} by {ModifiedBy}",
            LogDisplayName.WebUserInfo(entity.Email, userId), permissionLevel, modifiedBy);
    }

    public async Task SetActiveAsync(string userId, bool isActive, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (entity == null) return;

        entity.IsActive = isActive;
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Set {User} active status to {IsActive}", LogDisplayName.WebUserInfo(entity.Email, userId), isActive);
    }

    public async Task UpdateStatusAsync(string userId, UserStatus newStatus, string modifiedBy, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (entity == null) return;

        entity.Status = (DataModels.UserStatus)(int)newStatus;
        entity.IsActive = newStatus == UserStatus.Active;
        entity.ModifiedBy = modifiedBy;
        entity.ModifiedAt = DateTimeOffset.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Updated status for {User} to {Status} by {ModifiedBy}",
            LogDisplayName.WebUserInfo(entity.Email, userId), newStatus, modifiedBy);
    }

    public async Task UpdateAsync(UserRecord user, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.Users.FirstOrDefaultAsync(u => u.Id == user.Id, cancellationToken);
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

        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Updated user {User}", user.ToLogInfo());
    }

    // Account Lockout Methods (SECURITY-5, SECURITY-6)

    public async Task IncrementFailedLoginAttemptsAsync(string userId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (entity == null) return;

        entity.FailedLoginAttempts++;
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Incremented failed login attempts for {User} to {Attempts}",
            LogDisplayName.WebUserInfo(entity.Email, userId), entity.FailedLoginAttempts);
    }

    public async Task ResetFailedLoginAttemptsAsync(string userId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (entity == null) return;

        entity.FailedLoginAttempts = 0;
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Reset failed login attempts for {User}", LogDisplayName.WebUserInfo(entity.Email, userId));
    }

    public async Task LockAccountAsync(string userId, DateTimeOffset lockedUntil, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (entity == null) return;

        entity.LockedUntil = lockedUntil;
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogWarning("Locked account for {User} until {LockedUntil}",
            LogDisplayName.WebUserDebug(entity.Email, userId), lockedUntil);
    }

    public async Task UnlockAccountAsync(string userId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (entity == null) return;

        entity.LockedUntil = null;
        entity.FailedLoginAttempts = 0;
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Unlocked account for {User}", LogDisplayName.WebUserInfo(entity.Email, userId));
    }

    public async Task<string?> GetPrimaryOwnerEmailAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Get the first Owner (permission_level=2) by created_at - this is the primary/original owner
        var email = await context.Users
            .AsNoTracking()
            .Where(u => u.PermissionLevel == DataModels.PermissionLevel.Owner
                     && u.Status == DataModels.UserStatus.Active)
            .OrderBy(u => u.CreatedAt)
            .Select(u => u.Email)
            .FirstOrDefaultAsync(cancellationToken);

        return email;
    }
}
