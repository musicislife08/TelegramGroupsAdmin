namespace TelegramGroupsAdmin.Services.Auth;

/// <summary>
/// Service for managing account lockout after failed login attempts
/// </summary>
public interface IAccountLockoutService
{
    /// <summary>
    /// Handles a failed login attempt - increments counter and locks account if threshold reached
    /// </summary>
    Task HandleFailedLoginAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resets lockout state after successful login
    /// </summary>
    Task ResetLockoutAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Manually unlocks an account (admin action)
    /// </summary>
    Task UnlockAccountAsync(string userId, string unlockedBy, CancellationToken cancellationToken = default);
}
