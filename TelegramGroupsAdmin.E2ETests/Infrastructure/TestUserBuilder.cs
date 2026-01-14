using Microsoft.Extensions.DependencyInjection;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data.Services;
using TelegramGroupsAdmin.Repositories;
using TelegramGroupsAdmin.Services.Auth;
using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.E2ETests.Infrastructure;

/// <summary>
/// Fluent builder for creating test users with various configurations.
/// Each test should build exactly the users it needs with specific states.
/// </summary>
/// <remarks>
/// Example usage:
/// <code>
/// var password = TestCredentials.GeneratePassword();
/// var user = await new TestUserBuilder(Factory.Services)
///     .WithEmail("test@e2e.local")
///     .WithPassword(password)
///     .WithEmailVerified()
///     .BuildAsync();
/// </code>
/// </remarks>
public class TestUserBuilder
{
    private readonly IServiceProvider _services;
    private string? _email;
    private string? _password;
    private string? _passwordHash;
    private PermissionLevel _permissionLevel = PermissionLevel.Admin;
    private UserStatus _status = UserStatus.Active;
    private bool _emailVerified;
    private bool _totpEnabled;
    private string? _totpSecret;
    private DateTimeOffset? _lockedUntil;
    private int _failedLoginAttempts;

    public TestUserBuilder(IServiceProvider services)
    {
        _services = services;
    }

    /// <summary>
    /// Sets the user's email address.
    /// If not called, a random email will be generated.
    /// </summary>
    public TestUserBuilder WithEmail(string email)
    {
        _email = email;
        return this;
    }

    /// <summary>
    /// Sets the user's password (will be hashed during build).
    /// The plain password is stored in the returned TestUser for login use.
    /// </summary>
    public TestUserBuilder WithPassword(string password)
    {
        _password = password;
        return this;
    }

    /// <summary>
    /// Sets a pre-hashed password directly (for edge cases).
    /// Most tests should use WithStandardPassword() instead.
    /// </summary>
    public TestUserBuilder WithPasswordHash(string passwordHash)
    {
        _passwordHash = passwordHash;
        return this;
    }

    /// <summary>
    /// Uses the standard pre-hashed test password.
    /// This is ~2 seconds faster than WithPassword() because it skips PBKDF2 hashing.
    /// </summary>
    public TestUserBuilder WithStandardPassword()
    {
        _password = PrehashedTestCredentials.StandardPassword;
        _passwordHash = PrehashedTestCredentials.StandardPasswordHash;
        return this;
    }

    /// <summary>
    /// Uses the alternate pre-hashed test password.
    /// Useful for multi-user tests that need different credentials.
    /// </summary>
    public TestUserBuilder WithAlternatePassword()
    {
        _password = PrehashedTestCredentials.AlternatePassword;
        _passwordHash = PrehashedTestCredentials.AlternatePasswordHash;
        return this;
    }

    /// <summary>
    /// Sets the user's permission level.
    /// Default is Admin (lowest level).
    /// </summary>
    public TestUserBuilder WithPermissionLevel(PermissionLevel level)
    {
        _permissionLevel = level;
        return this;
    }

    /// <summary>
    /// Sets the user as Owner (highest permission level).
    /// </summary>
    public TestUserBuilder AsOwner() => WithPermissionLevel(PermissionLevel.Owner);

    /// <summary>
    /// Sets the user as GlobalAdmin.
    /// </summary>
    public TestUserBuilder AsGlobalAdmin() => WithPermissionLevel(PermissionLevel.GlobalAdmin);

    /// <summary>
    /// Sets the user as Admin (default).
    /// </summary>
    public TestUserBuilder AsAdmin() => WithPermissionLevel(PermissionLevel.Admin);

    /// <summary>
    /// Sets the user's status.
    /// Default is Active.
    /// </summary>
    public TestUserBuilder WithStatus(UserStatus status)
    {
        _status = status;
        return this;
    }

    /// <summary>
    /// Marks the user's email as verified.
    /// This is required for successful login.
    /// </summary>
    public TestUserBuilder WithEmailVerified(bool verified = true)
    {
        _emailVerified = verified;
        return this;
    }

    /// <summary>
    /// Configures TOTP (two-factor authentication) for the user.
    /// Note: Production defaults to TotpEnabled=true, but tests default to false for convenience.
    /// Use WithTotp(enabled: true) for 2FA tests, or WithTotpDisabled() to explicitly bypass.
    /// </summary>
    /// <param name="enabled">Whether TOTP is enabled</param>
    /// <param name="secret">Optional TOTP secret (if null and enabled, generates one)</param>
    public TestUserBuilder WithTotp(bool enabled = true, string? secret = null)
    {
        _totpEnabled = enabled;
        _totpSecret = enabled ? (secret ?? GenerateTotpSecret()) : null;
        return this;
    }

    /// <summary>
    /// Explicitly disables TOTP requirement for this test user.
    /// This simulates an owner-disabled 2FA scenario (production allows owners to disable 2FA for users).
    /// </summary>
    public TestUserBuilder WithTotpDisabled()
    {
        _totpEnabled = false;
        _totpSecret = null;
        return this;
    }

    /// <summary>
    /// Configures user to require TOTP setup on first login.
    /// Sets TotpEnabled=true but no secret, triggering /login/setup-2fa redirect.
    /// </summary>
    public TestUserBuilder RequiresTotpSetup()
    {
        _totpEnabled = true;
        _totpSecret = null; // No secret = needs setup
        return this;
    }

    /// <summary>
    /// Locks the user's account until the specified time.
    /// </summary>
    public TestUserBuilder LockedUntil(DateTimeOffset lockedUntil)
    {
        _lockedUntil = lockedUntil;
        _failedLoginAttempts = 5; // Typical lockout threshold
        return this;
    }

    /// <summary>
    /// Locks the user's account for a specified duration from now.
    /// </summary>
    public TestUserBuilder LockedFor(TimeSpan duration)
    {
        return LockedUntil(DateTimeOffset.UtcNow.Add(duration));
    }

    /// <summary>
    /// Sets the number of failed login attempts.
    /// </summary>
    public TestUserBuilder WithFailedLoginAttempts(int count)
    {
        _failedLoginAttempts = count;
        return this;
    }

    /// <summary>
    /// Builds and persists the user to the database.
    /// Returns a TestUser containing the user record and credentials for testing.
    /// </summary>
    public async Task<TestUser> BuildAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _services.CreateScope();
        var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var dataProtection = scope.ServiceProvider.GetRequiredService<IDataProtectionService>();

        // Generate defaults if not specified
        var email = _email ?? TestCredentials.GenerateEmail();
        var password = _password ?? TestCredentials.GeneratePassword();
        var hash = _passwordHash ?? passwordHasher.HashPassword(password);

        // TOTP secrets are encrypted in the database (app uses IDataProtectionService)
        // Store encrypted secret in DB, but return unencrypted for test code generation
        var unencryptedTotpSecret = _totpSecret;
        var encryptedTotpSecret = _totpSecret != null ? dataProtection.Protect(_totpSecret) : null;

        var userId = Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow;

        var userRecord = new UserRecord(
            Id: userId,
            Email: email,
            NormalizedEmail: email.ToUpperInvariant(),
            PasswordHash: hash,
            SecurityStamp: Guid.NewGuid().ToString(),
            PermissionLevel: _permissionLevel,
            InvitedBy: null,
            IsActive: _status == UserStatus.Active,
            TotpSecret: encryptedTotpSecret,
            TotpEnabled: _totpEnabled,
            TotpSetupStartedAt: null,
            CreatedAt: now,
            LastLoginAt: null,
            Status: _status,
            ModifiedBy: null,
            ModifiedAt: null,
            EmailVerified: _emailVerified,
            EmailVerificationToken: null,
            EmailVerificationTokenExpiresAt: null,
            PasswordResetToken: null,
            PasswordResetTokenExpiresAt: null,
            FailedLoginAttempts: _failedLoginAttempts,
            LockedUntil: _lockedUntil
        );

        await userRepository.CreateAsync(userRecord, cancellationToken);

        return new TestUser(userRecord, password, unencryptedTotpSecret);
    }

    private static string GenerateTotpSecret()
    {
        // Generate a 20-byte secret encoded as Base32 (standard TOTP secret length)
        var bytes = new byte[20];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return Base32Encode(bytes);
    }

    private static string Base32Encode(byte[] data)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var result = new System.Text.StringBuilder((data.Length * 8 + 4) / 5);

        int buffer = data[0];
        int next = 1;
        int bitsLeft = 8;

        while (bitsLeft > 0 || next < data.Length)
        {
            if (bitsLeft < 5)
            {
                if (next < data.Length)
                {
                    buffer <<= 8;
                    buffer |= data[next++] & 0xff;
                    bitsLeft += 8;
                }
                else
                {
                    int pad = 5 - bitsLeft;
                    buffer <<= pad;
                    bitsLeft += pad;
                }
            }

            int index = 0x1f & (buffer >> (bitsLeft - 5));
            bitsLeft -= 5;
            result.Append(alphabet[index]);
        }

        return result.ToString();
    }
}

/// <summary>
/// Represents a test user with credentials for E2E testing.
/// </summary>
public record TestUser(
    /// <summary>
    /// The user record as stored in the database.
    /// </summary>
    UserRecord Record,
    /// <summary>
    /// The plain-text password for login testing.
    /// </summary>
    string Password,
    /// <summary>
    /// The TOTP secret for 2FA testing (null if TOTP not configured).
    /// </summary>
    string? TotpSecret
)
{
    public string Id => Record.Id;
    public string Email => Record.Email;
    public TelegramGroupsAdmin.Core.Models.PermissionLevel PermissionLevel => Record.PermissionLevel;
}
