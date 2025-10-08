namespace TelegramGroupsAdmin.Services.Auth;

/// <summary>
/// Manages temporary authentication tokens issued after successful password verification
/// but before TOTP/2FA verification. These tokens prevent authentication bypass attacks.
/// </summary>
public interface IIntermediateAuthService
{
    /// <summary>
    /// Creates a temporary authentication token for a user who has successfully verified their password.
    /// Token is valid for 5 minutes and can only be used once.
    /// </summary>
    /// <param name="userId">The user ID who passed password verification</param>
    /// <returns>A cryptographically secure token string</returns>
    string CreateToken(string userId);

    /// <summary>
    /// Validates a temporary authentication token without consuming it.
    /// Use this to check if a token is valid before performing authentication.
    /// </summary>
    /// <param name="token">The token to validate</param>
    /// <param name="userId">The user ID that must match the token</param>
    /// <returns>True if token is valid and matches userId, false otherwise</returns>
    bool ValidateToken(string token, string userId);

    /// <summary>
    /// Consumes (removes) a temporary authentication token.
    /// Call this after successful authentication to prevent token reuse.
    /// </summary>
    /// <param name="token">The token to consume</param>
    void ConsumeToken(string token);

    /// <summary>
    /// Validates and consumes a temporary authentication token in a single operation.
    /// Token can only be used once - it is removed after successful validation.
    /// </summary>
    /// <param name="token">The token to validate</param>
    /// <param name="userId">The user ID that must match the token</param>
    /// <returns>True if token is valid and matches userId, false otherwise</returns>
    bool ValidateAndConsumeToken(string token, string userId);
}
