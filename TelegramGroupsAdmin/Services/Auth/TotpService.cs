using System.Security.Cryptography;
using System.Text;
using OtpNet;
using TelegramGroupsAdmin.Models;
using TelegramGroupsAdmin.Repositories;
using TelegramGroupsAdmin.Data.Services;

namespace TelegramGroupsAdmin.Services.Auth;

public class TotpService(
    UserRepository userRepository,
    IAuditService auditLog,
    ITotpProtectionService totpProtection,
    IPasswordHasher passwordHasher,
    ILogger<TotpService> logger)
    : ITotpService
{
    public async Task<TotpSetupResult> SetupTotpAsync(string userId, string userEmail, CancellationToken ct = default)
    {
        var user = await userRepository.GetByIdAsync(userId, ct);
        if (user is null)
        {
            throw new InvalidOperationException("User not found");
        }

        string secret;
        const int setupExpiryMinutes = 15;

        // SECURITY: Check if existing TOTP setup has expired (15min timeout)
        // This prevents abandoned setups from creating security issues
        bool setupExpired = false;
        if (user.TotpSetupStartedAt.HasValue)
        {
            var setupStartedAt = DateTimeOffset.FromUnixTimeSeconds(user.TotpSetupStartedAt.Value);
            var expiryTime = setupStartedAt.AddMinutes(setupExpiryMinutes);
            setupExpired = DateTimeOffset.UtcNow > expiryTime;

            if (setupExpired)
            {
                logger.LogInformation("TOTP setup expired for user {UserId} (started {SetupTime}, expired after {Minutes}min)",
                    userId, setupStartedAt, setupExpiryMinutes);
            }
        }

        // Reuse existing TOTP secret if:
        // 1. User has a TOTP secret
        // 2. TOTP is not enabled yet (setup in progress)
        // 3. Setup has not expired (< 15 minutes old)
        if (!string.IsNullOrEmpty(user.TotpSecret) && !user.TotpEnabled && !setupExpired)
        {
            // TOTP setup in progress - reuse existing secret (handles Blazor SSR page reloads)
            secret = totpProtection.Unprotect(user.TotpSecret);
            logger.LogDebug("Reusing existing TOTP secret for user {UserId} during setup", userId);
        }
        else
        {
            // Generate new TOTP secret if:
            // - No existing secret
            // - TOTP already enabled (user wants to reset)
            // - Setup expired (security: abandoned setup)
            secret = Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(20));

            // Encrypt and store secret (not enabled yet)
            var protectedSecret = totpProtection.Protect(secret);
            await userRepository.UpdateTotpSecretAsync(userId, protectedSecret, ct);
            logger.LogInformation("Generated new TOTP secret for user {UserId} (expired: {Expired})", userId, setupExpired);
        }

        // Generate QR code URI
        var qrCodeUri = $"otpauth://totp/Telegram Groups Admin:{Uri.EscapeDataString(userEmail)}?secret={secret}&issuer=Telegram Groups Admin";

        return new TotpSetupResult(secret, qrCodeUri, FormatSecretForManualEntry(secret));
    }

    public async Task<bool> VerifyAndEnableTotpAsync(string userId, string code, CancellationToken ct = default)
    {
        var user = await userRepository.GetByIdAsync(userId, ct);
        if (user is null || string.IsNullOrEmpty(user.TotpSecret))
        {
            return false;
        }

        // Decrypt TOTP secret for verification
        var totpSecret = totpProtection.Unprotect(user.TotpSecret);
        var totp = new Totp(Base32Encoding.ToBytes(totpSecret));

        if (!totp.VerifyTotp(code, out _, new VerificationWindow(2, 2)))
        {
            logger.LogWarning("Invalid TOTP verification code during setup for user: {UserId}", userId);
            return false;
        }

        // Enable TOTP
        await userRepository.EnableTotpAsync(userId, ct);

        // Update security stamp
        await userRepository.UpdateSecurityStampAsync(userId, ct);

        logger.LogInformation("TOTP enabled for user: {UserId}", userId);

        // Audit log
        await auditLog.LogEventAsync(
            AuditEventType.UserTotpEnabled,
            actorUserId: userId,
            targetUserId: userId,
            value: "TOTP 2FA enabled",
            ct: ct);

        return true;
    }

    public async Task<bool> VerifyTotpCodeAsync(string userId, string code, CancellationToken ct = default)
    {
        var user = await userRepository.GetByIdAsync(userId, ct);
        if (user is not { TotpEnabled: true } || string.IsNullOrEmpty(user.TotpSecret))
        {
            return false;
        }

        // Decrypt TOTP secret
        var totpSecret = totpProtection.Unprotect(user.TotpSecret);
        var totp = new Totp(Base32Encoding.ToBytes(totpSecret));

        if (!totp.VerifyTotp(code, out _, new VerificationWindow(2, 2)))
        {
            logger.LogWarning("Invalid TOTP code for user: {UserId}", userId);
            return false;
        }

        return true;
    }

    public async Task<bool> DisableTotpAsync(string userId, string password, CancellationToken ct = default)
    {
        var user = await userRepository.GetByIdAsync(userId, ct);
        if (user is null)
        {
            return false;
        }

        // Verify password before disabling TOTP
        if (!passwordHasher.VerifyPassword(password, user.PasswordHash))
        {
            logger.LogWarning("Invalid password attempt when disabling TOTP for user: {UserId}", userId);
            return false;
        }

        await userRepository.DisableTotpAsync(userId, ct);
        await userRepository.UpdateSecurityStampAsync(userId, ct);

        logger.LogInformation("TOTP disabled for user: {UserId}", userId);

        // Audit log
        await auditLog.LogEventAsync(
            AuditEventType.UserTotpDisabled,
            actorUserId: userId,
            targetUserId: userId,
            value: "TOTP 2FA disabled",
            ct: ct);

        return true;
    }

    public async Task<IReadOnlyList<string>> GenerateRecoveryCodesAsync(string userId, CancellationToken ct = default)
    {
        var codes = new List<string>();

        // Generate 8 recovery codes
        for (var i = 0; i < 8; i++)
        {
            var code = GenerateRecoveryCode();
            codes.Add(code);

            var codeHash = HashRecoveryCode(code);
            await userRepository.CreateRecoveryCodeAsync(userId, codeHash, ct);
        }

        logger.LogInformation("Generated {Count} recovery codes for user: {UserId}", codes.Count, userId);
        return codes.AsReadOnly();
    }

    public async Task<bool> UseRecoveryCodeAsync(string userId, string code, CancellationToken ct = default)
    {
        var codeHash = HashRecoveryCode(code);
        var isValid = await userRepository.UseRecoveryCodeAsync(userId, codeHash, ct);

        if (!isValid)
        {
            logger.LogWarning("Invalid recovery code attempt for user: {UserId}", userId);
            return false;
        }

        logger.LogInformation("Recovery code used for user: {UserId}", userId);
        return true;
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
        for (var i = 0; i < secret.Length; i++)
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
