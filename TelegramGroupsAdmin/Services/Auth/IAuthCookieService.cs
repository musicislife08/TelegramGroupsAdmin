namespace TelegramGroupsAdmin.Services.Auth;

/// <summary>
/// Service for generating authentication cookies and signing in/out users.
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
    /// <param name="user">The web user identity to authenticate</param>
    Task SignInAsync(HttpContext context, WebUserIdentity user);

    /// <summary>
    /// Signs out a user by clearing the authentication cookie.
    /// </summary>
    /// <param name="context">The HTTP context to sign out from</param>
    Task SignOutAsync(HttpContext context);

    /// <summary>
    /// Generates an encrypted cookie value without requiring HttpContext.
    /// Use this in tests to create valid auth cookies programmatically.
    /// </summary>
    /// <param name="user">The web user identity to generate a cookie for</param>
    /// <returns>The encrypted cookie value that can be set in a browser</returns>
    string GenerateCookieValue(WebUserIdentity user);

    /// <summary>
    /// Gets the name of the authentication cookie.
    /// </summary>
    string CookieName { get; }
}
