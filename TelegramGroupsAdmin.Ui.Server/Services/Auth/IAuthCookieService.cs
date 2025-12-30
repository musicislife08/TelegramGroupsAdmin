using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Ui.Server.Services.Auth;

/// <summary>
/// Service for generating authentication cookies and signing in users.
/// Centralizes all cookie authentication logic in one place for consistency
/// between the running application and test scenarios.
/// </summary>
public interface IAuthCookieService
{
    /// <summary>
    /// Signs in a user by setting the authentication cookie via HttpContext.
    /// Use this in the running application where HttpContext is available.
    /// </summary>
    /// <param name="context">The HTTP context to sign into</param>
    /// <param name="userId">The user's unique identifier</param>
    /// <param name="email">The user's email address</param>
    /// <param name="permissionLevel">The user's permission level</param>
    /// <param name="securityStamp">The user's security stamp for session invalidation</param>
    Task SignInAsync(HttpContext context, string userId, string email, PermissionLevel permissionLevel, string securityStamp);

    /// <summary>
    /// Generates an encrypted cookie value without requiring HttpContext.
    /// Use this in tests to create valid auth cookies programmatically.
    /// </summary>
    /// <param name="userId">The user's unique identifier</param>
    /// <param name="email">The user's email address</param>
    /// <param name="permissionLevel">The user's permission level</param>
    /// <param name="securityStamp">The user's security stamp for session invalidation</param>
    /// <returns>The encrypted cookie value that can be set in a browser</returns>
    string GenerateCookieValue(string userId, string email, PermissionLevel permissionLevel, string securityStamp);

    /// <summary>
    /// Gets the name of the authentication cookie.
    /// </summary>
    string CookieName { get; }
}
