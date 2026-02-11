using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Services.Auth;

/// <summary>
/// Service for managing account lockout after failed login attempts
/// </summary>
public interface IAccountLockoutService
{
    /// <summary>
    /// Handles a failed login attempt - increments counter and locks account if threshold reached
    /// </summary>
    Task HandleFailedLoginAsync(WebUserIdentity user, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resets lockout state after successful login
    /// </summary>
    Task ResetLockoutAsync(WebUserIdentity user, CancellationToken cancellationToken = default);

    /// <summary>
    /// Manually unlocks an account (admin action)
    /// </summary>
    Task UnlockAccountAsync(WebUserIdentity target, WebUserIdentity admin, CancellationToken cancellationToken = default);
}
