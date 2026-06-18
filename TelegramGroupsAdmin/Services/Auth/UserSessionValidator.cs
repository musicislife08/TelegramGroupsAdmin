using System.Security.Claims;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Auth;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Repositories;

namespace TelegramGroupsAdmin.Services.Auth;

public sealed class UserSessionValidator(
    IUserRepository userRepository,
    ILogger<UserSessionValidator> logger) : IUserSessionValidator
{
    public async Task<bool> IsStillValidAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var stamp = principal.FindFirst(CustomClaimTypes.SecurityStamp)?.Value;

        // Fail closed: a principal without both an id and a stamp cannot be validated.
        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(stamp))
            return false;

        try
        {
            var user = await userRepository.GetByIdAsync(userId, cancellationToken);
            if (user is null)
                return false;

            // Offboarding intent: only Active sessions survive. Disabled/Deleted/Pending are rejected.
            // (Deliberately NOT UserRecord.CanLogin, which also folds in transient lockout/email-verify.)
            if (user.Status != UserStatus.Active)
                return false;

            return string.Equals(user.SecurityStamp, stamp, StringComparison.Ordinal);
        }
        catch (Exception ex)
        {
            // Fail closed on any DB/transient error rather than allowing a stale session.
            logger.LogWarning(ex, "Session validation failed for user {UserId}; rejecting session", userId);
            return false;
        }
    }
}
