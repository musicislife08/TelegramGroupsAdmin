using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using OtpNet;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.Data.Repositories;
using TelegramGroupsAdmin.Data.Services;

namespace TelegramGroupsAdmin.Services;

public class AuthService : IAuthService
{
    private readonly UserRepository _userRepository;
    private readonly ITotpProtectionService _totpProtection;
    private readonly ILogger<AuthService> _logger;
    private const int Pbkdf2IterationCount = 100000;
    private const int Pbkdf2SubkeyLength = 32;
    private const int SaltSize = 16;

    public AuthService(
        UserRepository userRepository,
        ITotpProtectionService totpProtection,
        ILogger<AuthService> logger)
    {
        _userRepository = userRepository;
        _totpProtection = totpProtection;
        _logger = logger;
    }

    public async Task<AuthResult> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        var user = await _userRepository.GetByEmailAsync(email, ct);
        if (user == null)
        {
            _logger.LogWarning("Login attempt for non-existent email: {Email}", email);
            return new AuthResult(false, null, null, null, false, "Invalid email or password");
        }

        if (!user.IsActive)
        {
            _logger.LogWarning("Login attempt for inactive user: {UserId}", user.Id);
            return new AuthResult(false, null, null, null, false, "Account is inactive");
        }

        if (!VerifyPassword(password, user.PasswordHash))
        {
            _logger.LogWarning("Invalid password for user: {UserId}", user.Id);
            return new AuthResult(false, null, null, null, false, "Invalid email or password");
        }

        // Update last login timestamp
        await _userRepository.UpdateLastLoginAsync(user.Id, ct);

        // If TOTP is enabled, require verification
        if (user.TotpEnabled)
        {
            return new AuthResult(true, user.Id, user.Email, user.PermissionLevel, true, null);
        }

        return new AuthResult(true, user.Id, user.Email, user.PermissionLevel, false, null);
    }

    public async Task<AuthResult> VerifyTotpAsync(string userId, string code, CancellationToken ct = default)
    {
        var user = await _userRepository.GetByIdAsync(userId, ct);
        if (user == null || !user.TotpEnabled || string.IsNullOrEmpty(user.TotpSecret))
        {
            return new AuthResult(false, null, null, null, false, "TOTP not enabled");
        }

        // Decrypt TOTP secret
        var totpSecret = _totpProtection.Unprotect(user.TotpSecret);
        var totp = new Totp(Base32Encoding.ToBytes(totpSecret));

        if (!totp.VerifyTotp(code, out _, new VerificationWindow(2, 2)))
        {
            _logger.LogWarning("Invalid TOTP code for user: {UserId}", userId);
            return new AuthResult(false, null, null, null, false, "Invalid verification code");
        }

        await _userRepository.UpdateLastLoginAsync(userId, ct);
        return new AuthResult(true, user.Id, user.Email, user.PermissionLevel, false, null);
    }

    public async Task<bool> IsFirstRunAsync(CancellationToken ct = default)
    {
        return await _userRepository.GetUserCountAsync(ct) == 0;
    }

    public async Task<RegisterResult> RegisterAsync(string email, string password, string? inviteToken, CancellationToken ct = default)
    {
        // Check if this is first run (no users exist)
        var isFirstRun = await IsFirstRunAsync(ct);

        string? invitedBy = null;
        int permissionLevel;

        if (isFirstRun)
        {
            // First user gets Owner permissions automatically
            permissionLevel = 2; // Owner
            _logger.LogInformation("First run detected - creating owner account");
        }
        else
        {
            // Subsequent users require invite token
            if (string.IsNullOrEmpty(inviteToken))
            {
                return new RegisterResult(false, null, "Invite token is required");
            }

            // Validate invite token
            var invite = await _userRepository.GetInviteByTokenAsync(inviteToken, ct);
            if (invite == null)
            {
                return new RegisterResult(false, null, "Invalid invite token");
            }

            if (invite.UsedAt != null)
            {
                return new RegisterResult(false, null, "Invite token already used");
            }

            if (invite.ExpiresAt < DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            {
                return new RegisterResult(false, null, "Invite token expired");
            }

            invitedBy = invite.CreatedBy;
            permissionLevel = 0; // ReadOnly by default
        }

        // Check if email already exists
        var existing = await _userRepository.GetByEmailAsync(email, ct);
        if (existing != null)
        {
            return new RegisterResult(false, null, "Email already registered");
        }

        // Create user
        var userId = Guid.NewGuid().ToString();
        var passwordHash = HashPassword(password);
        var securityStamp = Guid.NewGuid().ToString();

        var user = new UserRecord(
            Id: userId,
            Email: email,
            NormalizedEmail: email.ToUpperInvariant(),
            PasswordHash: passwordHash,
            SecurityStamp: securityStamp,
            PermissionLevel: permissionLevel,
            InvitedBy: invitedBy,
            IsActive: true,
            TotpSecret: null,
            TotpEnabled: false,
            CreatedAt: DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            LastLoginAt: null
        );

        await _userRepository.CreateAsync(user, ct);

        // Mark invite as used (if not first run)
        if (!isFirstRun && !string.IsNullOrEmpty(inviteToken))
        {
            await _userRepository.UseInviteAsync(inviteToken, userId, ct);
            _logger.LogInformation("New user registered: {UserId} via invite from {InviterId}", userId, invitedBy);
        }
        else
        {
            _logger.LogInformation("Owner account created: {UserId} (first run)", userId);
        }

        return new RegisterResult(true, userId, null);
    }

    public async Task<TotpSetupResult> EnableTotpAsync(string userId, CancellationToken ct = default)
    {
        var user = await _userRepository.GetByIdAsync(userId, ct);
        if (user == null)
        {
            throw new InvalidOperationException("User not found");
        }

        // Generate new TOTP secret
        var secret = Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(20));

        // Generate QR code URI
        var qrCodeUri = $"otpauth://totp/TgSpam:{Uri.EscapeDataString(user.Email)}?secret={secret}&issuer=TgSpam";

        // Encrypt and store secret (not enabled yet)
        var protectedSecret = _totpProtection.Protect(secret);
        await _userRepository.UpdateTotpSecretAsync(userId, protectedSecret, ct);

        return new TotpSetupResult(secret, qrCodeUri, FormatSecretForManualEntry(secret));
    }

    public async Task<bool> VerifyAndEnableTotpAsync(string userId, string code, CancellationToken ct = default)
    {
        var user = await _userRepository.GetByIdAsync(userId, ct);
        if (user == null || string.IsNullOrEmpty(user.TotpSecret))
        {
            return false;
        }

        // Decrypt TOTP secret for verification
        var totpSecret = _totpProtection.Unprotect(user.TotpSecret);
        var totp = new Totp(Base32Encoding.ToBytes(totpSecret));

        if (!totp.VerifyTotp(code, out _, new VerificationWindow(2, 2)))
        {
            _logger.LogWarning("Invalid TOTP verification code during setup for user: {UserId}", userId);
            return false;
        }

        // Enable TOTP
        await _userRepository.EnableTotpAsync(userId, ct);

        // Update security stamp
        await _userRepository.UpdateSecurityStampAsync(userId, ct);

        _logger.LogInformation("TOTP enabled for user: {UserId}", userId);
        return true;
    }

    public async Task<bool> DisableTotpAsync(string userId, string password, CancellationToken ct = default)
    {
        var user = await _userRepository.GetByIdAsync(userId, ct);
        if (user == null)
        {
            return false;
        }

        // Verify password before disabling TOTP
        if (!VerifyPassword(password, user.PasswordHash))
        {
            _logger.LogWarning("Invalid password attempt when disabling TOTP for user: {UserId}", userId);
            return false;
        }

        await _userRepository.DisableTotpAsync(userId, ct);
        await _userRepository.UpdateSecurityStampAsync(userId, ct);

        _logger.LogInformation("TOTP disabled for user: {UserId}", userId);
        return true;
    }

    public async Task<IReadOnlyList<string>> GenerateRecoveryCodesAsync(string userId, CancellationToken ct = default)
    {
        var codes = new List<string>();

        // Generate 8 recovery codes
        for (int i = 0; i < 8; i++)
        {
            var code = GenerateRecoveryCode();
            codes.Add(code);

            var codeHash = HashRecoveryCode(code);
            await _userRepository.CreateRecoveryCodeAsync(userId, codeHash, ct);
        }

        _logger.LogInformation("Generated {Count} recovery codes for user: {UserId}", codes.Count, userId);
        return codes.AsReadOnly();
    }

    public async Task<AuthResult> UseRecoveryCodeAsync(string userId, string code, CancellationToken ct = default)
    {
        var user = await _userRepository.GetByIdAsync(userId, ct);
        if (user == null)
        {
            return new AuthResult(false, null, null, null, false, "Invalid recovery code");
        }

        var codeHash = HashRecoveryCode(code);
        var isValid = await _userRepository.UseRecoveryCodeAsync(userId, codeHash, ct);

        if (!isValid)
        {
            _logger.LogWarning("Invalid recovery code attempt for user: {UserId}", userId);
            return new AuthResult(false, null, null, null, false, "Invalid recovery code");
        }

        await _userRepository.UpdateLastLoginAsync(userId, ct);
        _logger.LogInformation("Recovery code used for user: {UserId}", userId);

        return new AuthResult(true, user.Id, user.Email, user.PermissionLevel, false, null);
    }

    public Task LogoutAsync(string userId, CancellationToken ct = default)
    {
        _logger.LogInformation("User logged out: {UserId}", userId);
        return Task.CompletedTask;
    }

    private static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var subkey = KeyDerivation.Pbkdf2(
            password: password,
            salt: salt,
            prf: KeyDerivationPrf.HMACSHA256,
            iterationCount: Pbkdf2IterationCount,
            numBytesRequested: Pbkdf2SubkeyLength
        );

        var outputBytes = new byte[1 + SaltSize + Pbkdf2SubkeyLength];
        outputBytes[0] = 0x01; // Version marker
        Buffer.BlockCopy(salt, 0, outputBytes, 1, SaltSize);
        Buffer.BlockCopy(subkey, 0, outputBytes, 1 + SaltSize, Pbkdf2SubkeyLength);

        return Convert.ToBase64String(outputBytes);
    }

    private static bool VerifyPassword(string password, string hashedPassword)
    {
        try
        {
            var decodedHash = Convert.FromBase64String(hashedPassword);

            if (decodedHash.Length != 1 + SaltSize + Pbkdf2SubkeyLength || decodedHash[0] != 0x01)
            {
                return false;
            }

            var salt = new byte[SaltSize];
            Buffer.BlockCopy(decodedHash, 1, salt, 0, SaltSize);

            var expectedSubkey = new byte[Pbkdf2SubkeyLength];
            Buffer.BlockCopy(decodedHash, 1 + SaltSize, expectedSubkey, 0, Pbkdf2SubkeyLength);

            var actualSubkey = KeyDerivation.Pbkdf2(
                password: password,
                salt: salt,
                prf: KeyDerivationPrf.HMACSHA256,
                iterationCount: Pbkdf2IterationCount,
                numBytesRequested: Pbkdf2SubkeyLength
            );

            return CryptographicOperations.FixedTimeEquals(actualSubkey, expectedSubkey);
        }
        catch
        {
            return false;
        }
    }

    private static string GenerateRecoveryCode()
    {
        var bytes = RandomNumberGenerator.GetBytes(8);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string HashRecoveryCode(string code)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(code.ToLowerInvariant()));
        return Convert.ToBase64String(bytes);
    }

    private static string FormatSecretForManualEntry(string secret)
    {
        // Format as groups of 4 characters for easier manual entry
        var formatted = new StringBuilder();
        for (int i = 0; i < secret.Length; i++)
        {
            if (i > 0 && i % 4 == 0)
            {
                formatted.Append(' ');
            }
            formatted.Append(secret[i]);
        }
        return formatted.ToString();
    }
}
