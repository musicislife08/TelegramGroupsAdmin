using System.Security.Cryptography;

namespace TelegramGroupsAdmin.E2ETests.Infrastructure;

/// <summary>
/// Generates test credentials that pass validation rules without triggering secret scanners.
/// Each test should generate its own password - no hardcoded test passwords.
/// </summary>
public static class TestCredentials
{
    private const int PasswordLength = 16;
    private const string LowercaseChars = "abcdefghijklmnopqrstuvwxyz";
    private const string UppercaseChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string DigitChars = "0123456789";
    private const string SpecialChars = "!@#$%^&*()-_=+";

    /// <summary>
    /// Generates a random password that meets typical complexity requirements:
    /// - At least 8 characters (we use 16 for safety)
    /// - Contains lowercase, uppercase, digit, and special characters
    /// </summary>
    /// <returns>A randomly generated password string</returns>
    public static string GeneratePassword()
    {
        // Ensure at least one of each required character type
        var password = new char[PasswordLength];
        password[0] = LowercaseChars[RandomNumberGenerator.GetInt32(LowercaseChars.Length)];
        password[1] = UppercaseChars[RandomNumberGenerator.GetInt32(UppercaseChars.Length)];
        password[2] = DigitChars[RandomNumberGenerator.GetInt32(DigitChars.Length)];
        password[3] = SpecialChars[RandomNumberGenerator.GetInt32(SpecialChars.Length)];

        // Fill remaining characters with random mix
        var allChars = LowercaseChars + UppercaseChars + DigitChars + SpecialChars;
        for (var i = 4; i < PasswordLength; i++)
        {
            password[i] = allChars[RandomNumberGenerator.GetInt32(allChars.Length)];
        }

        // Shuffle to avoid predictable pattern (first 4 chars always being specific types)
        Shuffle(password);

        return new string(password);
    }

    /// <summary>
    /// Generates a unique test email address.
    /// Uses a random suffix to ensure uniqueness across parallel test runs.
    /// </summary>
    /// <param name="prefix">Optional prefix for the email (default: "test")</param>
    /// <returns>A unique email address in the format prefix_randomsuffix@e2e.local</returns>
    public static string GenerateEmail(string prefix = "test")
    {
        var suffix = RandomNumberGenerator.GetHexString(8).ToLowerInvariant();
        return $"{prefix}_{suffix}@e2e.local";
    }

    private static void Shuffle(char[] array)
    {
        // Fisher-Yates shuffle using cryptographic RNG
        for (var i = array.Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (array[i], array[j]) = (array[j], array[i]);
        }
    }
}
