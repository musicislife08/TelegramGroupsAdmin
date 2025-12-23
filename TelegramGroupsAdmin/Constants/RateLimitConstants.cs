namespace TelegramGroupsAdmin.Constants;

/// <summary>
/// Constants for rate limiting authentication endpoints (SECURITY-5).
/// In-memory implementation suitable for single-instance deployments.
/// </summary>
public static class RateLimitConstants
{
    /// <summary>
    /// Login rate limit: 5 attempts per 15 minutes.
    /// </summary>
    public const int LoginMaxAttempts = 5;
    public static readonly TimeSpan LoginWindow = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Registration rate limit: 3 attempts per 1 hour.
    /// </summary>
    public const int RegisterMaxAttempts = 3;
    public static readonly TimeSpan RegisterWindow = TimeSpan.FromHours(1);

    /// <summary>
    /// TOTP verification rate limit: 5 attempts per 5 minutes.
    /// </summary>
    public const int TotpVerifyMaxAttempts = 5;
    public static readonly TimeSpan TotpVerifyWindow = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Recovery code verification rate limit: 5 attempts per 5 minutes.
    /// </summary>
    public const int RecoveryCodeMaxAttempts = 5;
    public static readonly TimeSpan RecoveryCodeWindow = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Resend email verification rate limit: 5 attempts per 1 hour.
    /// </summary>
    public const int ResendVerificationMaxAttempts = 5;
    public static readonly TimeSpan ResendVerificationWindow = TimeSpan.FromHours(1);

    /// <summary>
    /// Forgot password rate limit: 3 attempts per 1 hour.
    /// </summary>
    public const int ForgotPasswordMaxAttempts = 3;
    public static readonly TimeSpan ForgotPasswordWindow = TimeSpan.FromHours(1);

    /// <summary>
    /// Reset password rate limit: 3 attempts per 1 hour.
    /// </summary>
    public const int ResetPasswordMaxAttempts = 3;
    public static readonly TimeSpan ResetPasswordWindow = TimeSpan.FromHours(1);
}
