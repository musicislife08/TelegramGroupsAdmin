using TelegramGroupsAdmin.Core.Models;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Repositories;

/// <summary>
/// Repository for user management operations
/// </summary>
public interface IUserRepository
{
    Task<int> GetUserCountAsync(CancellationToken ct = default);
    Task<UiModels.UserRecord?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<UiModels.UserRecord?> GetByEmailIncludingDeletedAsync(string email, CancellationToken ct = default);
    Task<UiModels.UserRecord?> GetByIdAsync(string userId, CancellationToken ct = default);
    Task<string> CreateAsync(UiModels.UserRecord user, CancellationToken ct = default);

    /// <summary>
    /// Atomically register a user (create or reactivate) and mark the invite as used in a single transaction.
    /// - If email doesn't exist: creates new user with generated ID
    /// - If email exists (deleted user): reactivates with fresh credentials, resets TOTP/verification
    /// This prevents the scenario where user is created/updated but invite remains unused (allowing reuse).
    /// </summary>
    /// <param name="email">User's email (unique identifier for lookup)</param>
    /// <param name="passwordHash">Pre-hashed password</param>
    /// <param name="permissionLevel">Permission level from invite</param>
    /// <param name="invitedBy">ID of user who created the invite</param>
    /// <param name="inviteToken">The invite token to mark as used</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The user's ID (new or existing)</returns>
    Task<string> RegisterUserWithInviteAsync(
        string email,
        string passwordHash,
        PermissionLevel permissionLevel,
        string? invitedBy,
        string inviteToken,
        CancellationToken ct = default);
    Task UpdateLastLoginAsync(string userId, CancellationToken ct = default);
    Task UpdateSecurityStampAsync(string userId, CancellationToken ct = default);
    Task UpdateTotpSecretAsync(string userId, string totpSecret, CancellationToken ct = default);
    Task EnableTotpAsync(string userId, CancellationToken ct = default);
    Task DisableTotpAsync(string userId, CancellationToken ct = default);
    Task ResetTotpAsync(string userId, CancellationToken ct = default);
    Task DeleteRecoveryCodesAsync(string userId, CancellationToken ct = default);
    Task<List<UiModels.RecoveryCodeRecord>> GetRecoveryCodesAsync(string userId, CancellationToken cancellationToken = default);
    Task AddRecoveryCodesAsync(string userId, List<string> codeHashes, CancellationToken cancellationToken = default);
    Task CreateRecoveryCodeAsync(string userId, string codeHash, CancellationToken ct = default);
    Task<bool> UseRecoveryCodeAsync(string userId, string codeHash, CancellationToken ct = default);
    Task<UiModels.InviteRecord?> GetInviteByTokenAsync(string token, CancellationToken ct = default);
    Task UseInviteAsync(string token, string userId, CancellationToken ct = default);
    Task<List<UiModels.UserRecord>> GetAllAsync(CancellationToken ct = default);
    Task<List<UiModels.UserRecord>> GetAllIncludingDeletedAsync(CancellationToken ct = default);
    Task UpdatePermissionLevelAsync(string userId, int permissionLevel, string modifiedBy, CancellationToken ct = default);
    Task SetActiveAsync(string userId, bool isActive, CancellationToken ct = default);
    Task UpdateStatusAsync(string userId, UiModels.UserStatus newStatus, string modifiedBy, CancellationToken ct = default);
    Task UpdateAsync(UiModels.UserRecord user, CancellationToken ct = default);

    // Account Lockout Methods (SECURITY-5, SECURITY-6)
    Task IncrementFailedLoginAttemptsAsync(string userId, CancellationToken ct = default);
    Task ResetFailedLoginAttemptsAsync(string userId, CancellationToken ct = default);
    Task LockAccountAsync(string userId, DateTimeOffset lockedUntil, CancellationToken ct = default);
    Task UnlockAccountAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// Get the primary owner's email (first Owner account by created_at)
    /// Used for VAPID authentication subject in Web Push notifications
    /// </summary>
    Task<string?> GetPrimaryOwnerEmailAsync(CancellationToken ct = default);
}
