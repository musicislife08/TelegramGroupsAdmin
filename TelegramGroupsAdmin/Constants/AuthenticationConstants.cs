namespace TelegramGroupsAdmin.Constants;

/// <summary>
/// Constants for authentication and session management.
/// </summary>
public static class AuthenticationConstants
{
    /// <summary>
    /// Authentication cookie expiration time (30 days).
    /// </summary>
    public static readonly TimeSpan CookieExpiration = TimeSpan.FromDays(30);

    /// <summary>
    /// Intermediate authentication token lifetime (5 minutes) for TOTP/recovery code flows.
    /// </summary>
    public static readonly TimeSpan IntermediateTokenLifetime = TimeSpan.FromMinutes(5);

    /// <summary>
    /// TOTP setup expiration time (15 minutes).
    /// User must complete TOTP setup within this window.
    /// </summary>
    public static readonly TimeSpan TotpSetupExpiration = TimeSpan.FromMinutes(15);

    /// <summary>
    /// TOTP verification window (±5 steps = ±2.5 minutes).
    /// Allows for clock drift between server and authenticator app.
    /// </summary>
    public const int TotpVerificationWindowSteps = 5;

    /// <summary>
    /// Number of recovery codes to generate per user.
    /// </summary>
    public const int RecoveryCodeCount = 8;

    /// <summary>
    /// Recovery code byte length for cryptographic randomness.
    /// </summary>
    public const int RecoveryCodeByteLength = 8;

    /// <summary>
    /// Recovery code string length (hex encoding = 2 chars per byte).
    /// </summary>
    public const int RecoveryCodeStringLength = RecoveryCodeByteLength * 2; // 16 chars

    /// <summary>
    /// Intermediate auth token byte length for cryptographic randomness (32 bytes = 256 bits).
    /// </summary>
    public const int IntermediateTokenByteLength = 32;

    /// <summary>
    /// Email verification token byte length for cryptographic randomness.
    /// </summary>
    public const int VerificationTokenByteLength = 32;

    /// <summary>
    /// Email verification token expiration time (24 hours).
    /// </summary>
    public static readonly TimeSpan EmailVerificationTokenExpiration = TimeSpan.FromHours(24);

    /// <summary>
    /// Password reset token expiration time (1 hour).
    /// </summary>
    public static readonly TimeSpan PasswordResetTokenExpiration = TimeSpan.FromHours(1);

    /// <summary>
    /// TOTP secret key length in bytes (20 bytes for Base32 encoding).
    /// </summary>
    public const int TotpSecretKeyLength = 20;

    /// <summary>
    /// TOTP manual entry format group size (groups of 4 characters).
    /// </summary>
    public const int TotpManualEntryGroupSize = 4;
}
