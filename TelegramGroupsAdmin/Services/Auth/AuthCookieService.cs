using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;
using TelegramGroupsAdmin.Auth;
using TelegramGroupsAdmin.Constants;
using TelegramGroupsAdmin.Core.Models;

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
    public async Task SignInAsync(HttpContext context, string userId, string email, PermissionLevel permissionLevel)
    {
        var principal = CreateClaimsPrincipal(userId, email, permissionLevel);

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
    /// Generates an encrypted cookie value without requiring HttpContext.
    /// Use this in tests to create valid auth cookies programmatically.
    /// </summary>
    public string GenerateCookieValue(string userId, string email, PermissionLevel permissionLevel)
    {
        var options = _cookieOptions.Get(CookieAuthenticationDefaults.AuthenticationScheme);

        var principal = CreateClaimsPrincipal(userId, email, permissionLevel);

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
    private static ClaimsPrincipal CreateClaimsPrincipal(string userId, string email, PermissionLevel permissionLevel)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Email, email),
            new(ClaimTypes.Role, GetRoleName(permissionLevel)),
            new(CustomClaimTypes.PermissionLevel, ((int)permissionLevel).ToString())
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
