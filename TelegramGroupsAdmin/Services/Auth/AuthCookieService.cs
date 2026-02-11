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
    public async Task SignInAsync(HttpContext context, WebUserIdentity user)
    {
        var principal = CreateClaimsPrincipal(user);

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
    public string GenerateCookieValue(WebUserIdentity user)
    {
        var options = _cookieOptions.Get(CookieAuthenticationDefaults.AuthenticationScheme);

        var principal = CreateClaimsPrincipal(user);

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
    private static ClaimsPrincipal CreateClaimsPrincipal(WebUserIdentity user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Email, user.Email ?? ""),
            new(ClaimTypes.Role, GetRoleName(user.PermissionLevel)),
            new(CustomClaimTypes.PermissionLevel, ((int)user.PermissionLevel).ToString())
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        return new ClaimsPrincipal(identity);
    }

    /// <summary>
    /// Converts permission level enum to role name string.
    /// </summary>
    private static string GetRoleName(PermissionLevel permissionLevel) => permissionLevel switch
    {
        PermissionLevel.Admin => "Admin",
        PermissionLevel.GlobalAdmin => "GlobalAdmin",
        PermissionLevel.Owner => "Owner",
        _ => "Admin" // Default to lowest permission level
    };
}
