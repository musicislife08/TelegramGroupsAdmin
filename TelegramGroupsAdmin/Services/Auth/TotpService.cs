using System.Security.Cryptography;
using System.Text;
using OtpNet;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Data.Services;

namespace TelegramGroupsAdmin.Services.Auth;

public class TotpService(
    IUserRepository userRepository,
    IAuditService auditLog,
    IDataProtectionService totpProtection,
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
            var setupStartedAt = user.TotpSetupStartedAt.Value;
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

    public async Task<TotpVerificationResult> VerifyAndEnableTotpAsync(string userId, string code, CancellationToken ct = default)
    {
        var user = await userRepository.GetByIdAsync(userId, ct);
        if (user is null || string.IsNullOrEmpty(user.TotpSecret))
        {
            return new TotpVerificationResult(false, false, "User not found or TOTP not configured");
        }

        // Check if setup has expired (user may be using code from old secret)
        const int setupExpiryMinutes = 15;
        bool setupExpired = false;
        if (user.TotpSetupStartedAt.HasValue)
        {
            var setupStartedAt = user.TotpSetupStartedAt.Value;
            var expiryTime = setupStartedAt.AddMinutes(setupExpiryMinutes);
            setupExpired = DateTimeOffset.UtcNow > expiryTime;
        }

        // Decrypt TOTP secret for verification
        var totpSecret = totpProtection.Unprotect(user.TotpSecret);
        var totp = new Totp(Base32Encoding.ToBytes(totpSecret));

        // Increased tolerance to ±2.5 minutes (5 steps) to handle real-world clock drift
        // Testing showed 1-2 minute drift is common on mobile devices between NTP syncs
        if (!totp.VerifyTotp(code, out _, new VerificationWindow(5, 5)))
        {
            if (setupExpired)
            {
                logger.LogWarning("Invalid TOTP verification code during setup for user {UserId} (setup expired - user may need to re-scan QR code)", userId);
                return new TotpVerificationResult(
                    false,
                    true,
                    "Your TOTP setup expired. Please scan the QR code again with your authenticator app.");
            }
            else
            {
                logger.LogWarning("Invalid TOTP verification code during setup for user: {UserId}", userId);
                return new TotpVerificationResult(false, false, "Invalid verification code. Please try again.");
            }
        }

        // Enable TOTP
        await userRepository.EnableTotpAsync(userId, ct);

        // Update security stamp
        await userRepository.UpdateSecurityStampAsync(userId, ct);

        logger.LogInformation("TOTP enabled for user: {UserId}", userId);

        // Audit log
        await auditLog.LogEventAsync(
            AuditEventType.UserTotpEnabled,
            actor: Actor.FromWebUser(userId),
            target: Actor.FromWebUser(userId),
            value: "TOTP 2FA enabled",
            ct: ct);

        return new TotpVerificationResult(true, false, null);
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

        // Increased tolerance to ±2.5 minutes (5 steps) to handle real-world clock drift
        // Testing showed 1-2 minute drift is common on mobile devices between NTP syncs
        if (!totp.VerifyTotp(code, out _, new VerificationWindow(5, 5)))
        {
            logger.LogWarning("Invalid TOTP code for user: {UserId}", userId);

            // Audit log for security monitoring (track brute force attempts)
            await auditLog.LogEventAsync(
                AuditEventType.UserTotpVerificationFailed,
                actor: Actor.FromWebUser(userId),
                target: Actor.FromWebUser(userId),
                value: "Invalid TOTP code entered",
                ct: ct);

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
            AuditEventType.UserTotpReset,
            actor: Actor.FromWebUser(userId),
            target: Actor.FromWebUser(userId),
            value: "TOTP 2FA disabled by user",
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

            // Audit log for security monitoring (track brute force attempts)
            await auditLog.LogEventAsync(
                AuditEventType.UserRecoveryCodeVerificationFailed,
                actor: Actor.FromWebUser(userId),
                target: Actor.FromWebUser(userId),
                value: "Invalid recovery code entered",
                ct: ct);

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
