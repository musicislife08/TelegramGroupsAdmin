namespace TelegramGroupsAdmin.Repositories;

/// <summary>
/// Repository for user management operations
/// </summary>
public interface IUserRepository
{
    Task<int> GetUserCountAsync(CancellationToken cancellationToken = default);
    Task<UserRecord?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<UserRecord?> GetByEmailIncludingDeletedAsync(string email, CancellationToken cancellationToken = default);
    Task<UserRecord?> GetByIdAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets multiple users by their IDs in a single query.
    /// Used for batch hydration to avoid N+1 query patterns.
    /// </summary>
    Task<List<UserRecord>> GetByIdsAsync(IEnumerable<string> userIds, CancellationToken cancellationToken = default);

    Task<string> CreateAsync(UserRecord user, CancellationToken cancellationToken = default);

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
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The user's ID (new or existing)</returns>
    Task<string> RegisterUserWithInviteAsync(
        string email,
        string passwordHash,
        PermissionLevel permissionLevel,
        string? invitedBy,
        string inviteToken,
        CancellationToken cancellationToken = default);
    Task UpdateLastLoginAsync(string userId, CancellationToken cancellationToken = default);
    Task UpdateSecurityStampAsync(string userId, CancellationToken cancellationToken = default);
    Task UpdateTotpSecretAsync(string userId, string totpSecret, CancellationToken cancellationToken = default);
    Task EnableTotpAsync(string userId, CancellationToken cancellationToken = default);
    Task DisableTotpAsync(string userId, CancellationToken cancellationToken = default);
    Task ResetTotpAsync(string userId, CancellationToken cancellationToken = default);
    Task DeleteRecoveryCodesAsync(string userId, CancellationToken cancellationToken = default);
    Task<List<RecoveryCodeRecord>> GetRecoveryCodesAsync(string userId, CancellationToken cancellationToken = default);
    Task AddRecoveryCodesAsync(string userId, List<string> codeHashes, CancellationToken cancellationToken = default);
    Task CreateRecoveryCodeAsync(string userId, string codeHash, CancellationToken cancellationToken = default);
    Task<bool> UseRecoveryCodeAsync(string userId, string codeHash, CancellationToken cancellationToken = default);
    Task<InviteRecord?> GetInviteByTokenAsync(string token, CancellationToken cancellationToken = default);
    Task UseInviteAsync(string token, string userId, CancellationToken cancellationToken = default);
    Task<List<UserRecord>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<List<UserRecord>> GetAllIncludingDeletedAsync(CancellationToken cancellationToken = default);
    Task UpdatePermissionLevelAsync(string userId, int permissionLevel, string modifiedBy, CancellationToken cancellationToken = default);
    Task SetActiveAsync(string userId, bool isActive, CancellationToken cancellationToken = default);
    Task UpdateStatusAsync(string userId, UserStatus newStatus, string modifiedBy, CancellationToken cancellationToken = default);
    Task UpdateAsync(UserRecord user, CancellationToken cancellationToken = default);

    // Account Lockout Methods (SECURITY-5, SECURITY-6)
    Task IncrementFailedLoginAttemptsAsync(string userId, CancellationToken cancellationToken = default);
    Task ResetFailedLoginAttemptsAsync(string userId, CancellationToken cancellationToken = default);
    Task LockAccountAsync(string userId, DateTimeOffset lockedUntil, CancellationToken cancellationToken = default);
    Task UnlockAccountAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the primary owner's email (first Owner account by created_at)
    /// Used for VAPID authentication subject in Web Push notifications
    /// </summary>
    Task<string?> GetPrimaryOwnerEmailAsync(CancellationToken cancellationToken = default);
}
