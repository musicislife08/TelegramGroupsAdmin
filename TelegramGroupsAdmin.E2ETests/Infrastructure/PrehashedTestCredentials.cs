namespace TelegramGroupsAdmin.E2ETests.Infrastructure;

/// <summary>
/// Pre-computed password hashes for E2E tests.
/// Eliminates ~2s PBKDF2 hashing overhead per user creation.
///
/// SECURITY NOTE: These are test-only credentials. The hashes are safe to
/// commit because they use the same algorithm as production but are only
/// valid for the specific plaintext passwords defined here.
/// </summary>
public static class PrehashedTestCredentials
{
    /// <summary>
    /// Standard test password meeting complexity requirements.
    /// Used by default for all test user creation.
    /// </summary>
    public const string StandardPassword = "Passw0rd!SaidNoSecurityAuditorEver";

    /// <summary>
    /// Pre-computed PBKDF2 hash of StandardPassword.
    /// Generated once and reused across all tests.
    /// </summary>
    public const string StandardPasswordHash = "Aaj7PPDzJOooJ8i0q5DvlCia+/EkSS1bw9C7Zvb/bJAH43NzWn8SrH6DesRWs+PSYA==";

    /// <summary>
    /// Alternative password for multi-user tests that need different credentials.
    /// xkcd.com/936 would be proud (but we still need the special chars)
    /// </summary>
    public const string AlternatePassword = "CorrectHorseBatteryStaple!Lol1";
    public const string AlternatePasswordHash = "AUcvve9Y+BJaDtdXgk1Sun0G1u1utzhkdR6YRgMemfQ4pga2mod5AH/N5+wpBq7ApQ==";

    /// <summary>
    /// Weak password for testing password strength validation.
    /// </summary>
    public const string WeakPassword = "password";
    // No hash - this is intentionally for rejection testing
}
