using OtpNet;

namespace TelegramGroupsAdmin.E2ETests.Helpers;

/// <summary>
/// Helper class for generating TOTP codes in E2E tests.
/// Uses the Otp.NET library with standard TOTP settings (6 digits, 30-second window).
/// </summary>
public static class TotpHelper
{
    /// <summary>
    /// Generates a valid TOTP code for the given Base32-encoded secret.
    /// </summary>
    /// <param name="base32Secret">The Base32-encoded TOTP secret</param>
    /// <returns>A 6-digit TOTP code valid for the current 30-second window</returns>
    public static string GenerateCode(string base32Secret)
    {
        var secretBytes = Base32Encoding.ToBytes(base32Secret);
        var totp = new Totp(secretBytes);
        return totp.ComputeTotp();
    }

    /// <summary>
    /// Generates a TOTP code for a specific timestamp (useful for testing edge cases).
    /// </summary>
    /// <param name="base32Secret">The Base32-encoded TOTP secret</param>
    /// <param name="timestamp">The timestamp to generate the code for</param>
    /// <returns>A 6-digit TOTP code valid for the timestamp's 30-second window</returns>
    public static string GenerateCode(string base32Secret, DateTime timestamp)
    {
        var secretBytes = Base32Encoding.ToBytes(base32Secret);
        var totp = new Totp(secretBytes);
        return totp.ComputeTotp(timestamp);
    }

    /// <summary>
    /// Validates a TOTP code against the given secret.
    /// Uses the same ±2.5 minute tolerance window (5 steps) as the production app
    /// to handle real-world clock drift on mobile devices.
    /// </summary>
    /// <param name="base32Secret">The Base32-encoded TOTP secret</param>
    /// <param name="code">The 6-digit code to validate</param>
    /// <returns>True if the code is valid, false otherwise</returns>
    public static bool ValidateCode(string base32Secret, string code)
    {
        var secretBytes = Base32Encoding.ToBytes(base32Secret);
        var totp = new Totp(secretBytes);
        // Match production: VerificationWindow(5, 5) = ±2.5 minutes (5 steps × 30 seconds)
        return totp.VerifyTotp(code, out _, new VerificationWindow(5, 5));
    }
}
