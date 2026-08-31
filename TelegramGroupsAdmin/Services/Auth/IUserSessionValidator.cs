using System.Security.Claims;

namespace TelegramGroupsAdmin.Services.Auth;

/// <summary>
/// Single source of truth for whether an authenticated principal still corresponds to a
/// valid, active DB user with a matching security stamp. Called by both the cookie
/// OnValidatePrincipal handler (HTTP edge) and the in-circuit revalidating provider.
/// </summary>
public interface IUserSessionValidator
{
    Task<bool> IsStillValidAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default);
}
