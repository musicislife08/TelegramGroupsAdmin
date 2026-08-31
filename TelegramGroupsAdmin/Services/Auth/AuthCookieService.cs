using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;
using TelegramGroupsAdmin.Auth;
using TelegramGroupsAdmin.Constants;

namespace TelegramGroupsAdmin.Services.Auth;

/// <summary>
/// Service for generating authentication cookies and signing in users.
/// Centralizes all cookie authentication logic in one place for consistency
/// between the running application and test scenarios.
/// </summary>
public class AuthCookieService : IAuthCookieService
{
    private readonly IOptionsMonitor<CookieAuthenticationOptions> _cookieOptions;

    /// <summary>
    /// The name of the authentication cookie.
    /// </summary>
    public string CookieName => "TgSpam.Auth";

    public AuthCookieService(IOptionsMonitor<CookieAuthenticationOptions> cookieOptions)
    {
        _cookieOptions = cookieOptions;
    }

    /// <summary>
    /// Signs in a user by setting the authentication cookie via HttpContext.
    /// Use this in the running application where HttpContext is available.
    /// </summary>
    public async Task SignInAsync(HttpContext context, WebUserIdentity user, string securityStamp)
    {
        var principal = CreateClaimsPrincipal(user, securityStamp);

        await context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.Add(AuthenticationConstants.CookieExpiration)
            });
    }

    /// <summary>
    /// Signs out a user by clearing the authentication cookie.
    /// </summary>
    public async Task SignOutAsync(HttpContext context)
    {
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// Generates an encrypted cookie value without requiring HttpContext.
    /// Use this in tests to create valid auth cookies programmatically.
    /// </summary>
    public string GenerateCookieValue(WebUserIdentity user, string securityStamp)
    {
        var options = _cookieOptions.Get(CookieAuthenticationDefaults.AuthenticationScheme);

        var principal = CreateClaimsPrincipal(user, securityStamp);

        var ticket = new AuthenticationTicket(
            principal,
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.Add(AuthenticationConstants.CookieExpiration),
                IssuedUtc = DateTimeOffset.UtcNow
            },
            CookieAuthenticationDefaults.AuthenticationScheme);

        // Use the same TicketDataFormat the app uses to encrypt cookies
        return options.TicketDataFormat.Protect(ticket);
    }

    /// <summary>
    /// Creates a ClaimsPrincipal with the standard claims used for authentication.
    /// This is the single source of truth for what claims are included in auth cookies.
    /// </summary>
    private static ClaimsPrincipal CreateClaimsPrincipal(WebUserIdentity user, string securityStamp)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Email, user.Email ?? ""),
            new(ClaimTypes.Role, user.PermissionLevel.GetDisplayName()),
            new(CustomClaimTypes.PermissionLevel, ((int)user.PermissionLevel).ToString()),
            new(CustomClaimTypes.SecurityStamp, securityStamp)
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme,
            nameType: ClaimTypes.Email, roleType: ClaimTypes.Role);
        return new ClaimsPrincipal(identity);
    }

}
