using System.Security.Cryptography;
using System.Text;
using OtpNet;
using TelegramGroupsAdmin.Constants;
using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Data.Services;
using TelegramGroupsAdmin.Repositories;

namespace TelegramGroupsAdmin.Services.Auth;

public class TotpService(
    IUserRepository userRepository,
    IAuditService auditLog,
    IDataProtectionService totpProtection,
    ILogger<TotpService> logger)
    : ITotpService
{
    public async Task<TotpSetupResult> SetupTotpAsync(WebUserIdentity user, CancellationToken cancellationToken = default)
    {
        var dbUser = await userRepository.GetByIdAsync(user.Id, cancellationToken);
        if (dbUser is null)
        {
            throw new InvalidOperationException("User not found");
        }

        string secret;

        // SECURITY: Check if existing TOTP setup has expired (15min timeout)
        // This prevents abandoned setups from creating security issues
        bool setupExpired = false;
        if (dbUser.TotpSetupStartedAt.HasValue)
        {
            var setupStartedAt = dbUser.TotpSetupStartedAt.Value;
            var expiryTime = setupStartedAt.Add(AuthenticationConstants.TotpSetupExpiration);
            setupExpired = DateTimeOffset.UtcNow > expiryTime;

            if (setupExpired)
            {
                logger.LogInformation("TOTP setup expired for {User} (started {SetupTime}, expired after {Minutes}min)",
                    user.ToLogInfo(), setupStartedAt, AuthenticationConstants.TotpSetupExpiration.TotalMinutes);
            }
        }

        // Reuse existing TOTP secret if:
        // 1. User has a TOTP secret
        // 2. Setup is in progress (TotpSetupStartedAt is set - secret generated but not verified yet)
        // 3. Setup has not expired (< 15 minutes old)
        // Note: We check TotpSetupStartedAt instead of !TotpEnabled because TotpEnabled=true by default
        if (!string.IsNullOrEmpty(dbUser.TotpSecret) && dbUser.TotpSetupStartedAt.HasValue && !setupExpired)
        {
            // TOTP setup in progress - reuse existing secret (handles Blazor SSR page reloads)
            secret = totpProtection.Unprotect(dbUser.TotpSecret);
            logger.LogDebug("Reusing existing TOTP secret for {User} during setup",
                user.ToLogDebug());
        }
        else
        {
            // Generate new TOTP secret if:
            // - No existing secret (first time setup)
            // - Setup completed (TotpSetupStartedAt cleared - user wants to reset)
            // - Setup expired (security: abandoned setup after 15min)
            secret = Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(AuthenticationConstants.TotpSecretKeyLength));

            // Encrypt and store secret (not enabled yet)
            var protectedSecret = totpProtection.Protect(secret);
            await userRepository.UpdateTotpSecretAsync(user.Id, protectedSecret, cancellationToken);
            logger.LogInformation("Generated new TOTP secret for {User} (expired: {Expired})",
                user.ToLogInfo(), setupExpired);
        }

        // Generate QR code URI
        var email = user.Email ?? user.Id;
        var qrCodeUri = $"otpauth://totp/Telegram Groups Admin:{Uri.EscapeDataString(email)}?secret={secret}&issuer=Telegram Groups Admin";

        return new TotpSetupResult(secret, qrCodeUri, FormatSecretForManualEntry(secret));
    }

    public async Task<TotpVerificationResult> VerifyAndEnableTotpAsync(WebUserIdentity user, string code, CancellationToken cancellationToken = default)
    {
        var dbUser = await userRepository.GetByIdAsync(user.Id, cancellationToken);
        if (dbUser is null || string.IsNullOrEmpty(dbUser.TotpSecret))
        {
            return new TotpVerificationResult(false, false, "User not found or TOTP not configured");
        }

        // Check if setup has expired (user may be using code from old secret)
        bool setupExpired = false;
        if (dbUser.TotpSetupStartedAt.HasValue)
        {
            var setupStartedAt = dbUser.TotpSetupStartedAt.Value;
            var expiryTime = setupStartedAt.Add(AuthenticationConstants.TotpSetupExpiration);
            setupExpired = DateTimeOffset.UtcNow > expiryTime;
        }

        // Decrypt TOTP secret for verification
        var totpSecret = totpProtection.Unprotect(dbUser.TotpSecret);
        var totp = new Totp(Base32Encoding.ToBytes(totpSecret));

        // Increased tolerance to ±2.5 minutes (5 steps) to handle real-world clock drift
        // Testing showed 1-2 minute drift is common on mobile devices between NTP syncs
        if (!totp.VerifyTotp(code, out _, new VerificationWindow(AuthenticationConstants.TotpVerificationWindowSteps, AuthenticationConstants.TotpVerificationWindowSteps)))
        {
            if (setupExpired)
            {
                logger.LogWarning("Invalid TOTP verification code during setup for {User} (setup expired - user may need to re-scan QR code)",
                    user.ToLogDebug());
                return new TotpVerificationResult(
                    false,
                    true,
                    "Your TOTP setup expired. Please scan the QR code again with your authenticator app.");
            }
            else
            {
                logger.LogWarning("Invalid TOTP verification code during setup for {User}",
                    user.ToLogDebug());
                return new TotpVerificationResult(false, false, "Invalid verification code. Please try again.");
            }
        }

        // Enable TOTP
        await userRepository.EnableTotpAsync(user.Id, cancellationToken);

        // Update security stamp
        await userRepository.UpdateSecurityStampAsync(user.Id, cancellationToken);

        logger.LogInformation("TOTP enabled for {User}", user.ToLogInfo());

        // Audit log
        var actor = user.ToActor();
        await auditLog.LogEventAsync(
            AuditEventType.UserTotpEnabled,
            actor: actor,
            target: actor,
            value: "TOTP 2FA enabled",
            cancellationToken: cancellationToken);

        return new TotpVerificationResult(true, false, null);
    }

    public async Task<bool> VerifyTotpCodeAsync(WebUserIdentity user, string code, CancellationToken cancellationToken = default)
    {
        var dbUser = await userRepository.GetByIdAsync(user.Id, cancellationToken);
        if (dbUser is not { TotpEnabled: true } || string.IsNullOrEmpty(dbUser.TotpSecret))
        {
            return false;
        }

        // Decrypt TOTP secret
        var totpSecret = totpProtection.Unprotect(dbUser.TotpSecret);
        var totp = new Totp(Base32Encoding.ToBytes(totpSecret));

        // Increased tolerance to ±2.5 minutes (5 steps) to handle real-world clock drift
        // Testing showed 1-2 minute drift is common on mobile devices between NTP syncs
        if (!totp.VerifyTotp(code, out _, new VerificationWindow(AuthenticationConstants.TotpVerificationWindowSteps, AuthenticationConstants.TotpVerificationWindowSteps)))
        {
            logger.LogWarning("Invalid TOTP code for {User}", user.ToLogDebug());

            // Audit log for security monitoring (track brute force attempts)
            var actor = user.ToActor();
            await auditLog.LogEventAsync(
                AuditEventType.UserTotpVerificationFailed,
                actor: actor,
                target: actor,
                value: "Invalid TOTP code entered",
                cancellationToken: cancellationToken);

            return false;
        }

        return true;
    }

    public async Task<bool> AdminDisableTotpAsync(WebUserIdentity target, WebUserIdentity admin, CancellationToken cancellationToken = default)
    {
        // Verify admin is Owner
        if (!admin.IsOwner)
        {
            logger.LogWarning("Non-Owner {Admin} attempted to admin-disable TOTP for user {Target}",
                admin.ToLogDebug(), target.ToLogDebug());
            return false;
        }

        // Verify target user exists
        var targetUser = await userRepository.GetByIdAsync(target.Id, cancellationToken);
        if (targetUser is null)
        {
            logger.LogWarning("{Admin} attempted to disable TOTP for non-existent user {Target}",
                admin.ToLogDebug(), target.ToLogDebug());
            return false;
        }

        // Disable TOTP without password check (keeps secret for re-enable)
        await userRepository.DisableTotpAsync(target.Id, cancellationToken);
        await userRepository.UpdateSecurityStampAsync(target.Id, cancellationToken);

        logger.LogWarning("TOTP admin-disabled for {Target} by Owner {Admin}",
            target.ToLogDebug(),
            admin.ToLogDebug());

        // Audit log
        await auditLog.LogEventAsync(
            AuditEventType.UserTotpReset,
            actor: admin.ToActor(),
            target: target.ToActor(),
            value: $"TOTP 2FA disabled by Owner admin override (admin: {admin.Email})",
            cancellationToken: cancellationToken);

        return true;
    }

    public async Task<bool> AdminEnableTotpAsync(WebUserIdentity target, WebUserIdentity admin, CancellationToken cancellationToken = default)
    {
        // Verify admin is Owner
        if (!admin.IsOwner)
        {
            logger.LogWarning("Non-Owner {Admin} attempted to admin-enable TOTP for user {Target}",
                admin.ToLogDebug(), target.ToLogDebug());
            return false;
        }

        // Verify target user exists
        var targetUser = await userRepository.GetByIdAsync(target.Id, cancellationToken);
        if (targetUser is null)
        {
            logger.LogWarning("{Admin} attempted to enable TOTP for non-existent user {Target}",
                admin.ToLogDebug(), target.ToLogDebug());
            return false;
        }

        // Enable TOTP (works even without secret - forces setup on next login)
        await userRepository.EnableTotpAsync(target.Id, cancellationToken);
        await userRepository.UpdateSecurityStampAsync(target.Id, cancellationToken);

        logger.LogWarning("TOTP admin-enabled for {Target} by Owner {Admin}",
            target.ToLogDebug(),
            admin.ToLogDebug());

        // Audit log
        await auditLog.LogEventAsync(
            AuditEventType.UserTotpEnabled,
            actor: admin.ToActor(),
            target: target.ToActor(),
            value: $"TOTP 2FA enabled by Owner admin override (admin: {admin.Email})",
            cancellationToken: cancellationToken);

        return true;
    }

    public async Task<bool> AdminResetTotpAsync(WebUserIdentity target, WebUserIdentity admin, CancellationToken cancellationToken = default)
    {
        // Verify admin is Owner
        if (!admin.IsOwner)
        {
            logger.LogWarning("Non-Owner {Admin} attempted to reset TOTP for user {Target}",
                admin.ToLogDebug(), target.ToLogDebug());
            return false;
        }

        // Verify target user exists
        var targetUser = await userRepository.GetByIdAsync(target.Id, cancellationToken);
        if (targetUser is null)
        {
            logger.LogWarning("{Admin} attempted to reset TOTP for non-existent user {Target}",
                admin.ToLogDebug(), target.ToLogDebug());
            return false;
        }

        // Reset TOTP completely (wipes secret, timestamp, and sets enabled=false)
        await userRepository.ResetTotpAsync(target.Id, cancellationToken);
        await userRepository.UpdateSecurityStampAsync(target.Id, cancellationToken);

        logger.LogWarning("TOTP reset for {Target} by Owner {Admin}",
            target.ToLogDebug(),
            admin.ToLogDebug());

        // Audit log
        await auditLog.LogEventAsync(
            AuditEventType.UserTotpReset,
            actor: admin.ToActor(),
            target: target.ToActor(),
            value: $"TOTP reset by Owner admin (admin: {admin.Email})",
            cancellationToken: cancellationToken);

        return true;
    }

    public async Task<IReadOnlyList<string>> GenerateRecoveryCodesAsync(WebUserIdentity user, CancellationToken cancellationToken = default)
    {
        var codes = new List<string>();

        // Generate recovery codes
        for (var i = 0; i < AuthenticationConstants.RecoveryCodeCount; i++)
        {
            var code = GenerateRecoveryCode();
            codes.Add(code);

            var codeHash = HashRecoveryCode(code);
            await userRepository.CreateRecoveryCodeAsync(user.Id, codeHash, cancellationToken);
        }

        logger.LogInformation("Generated {Count} recovery codes for {User}", codes.Count, user.ToLogInfo());
        return codes.AsReadOnly();
    }

    public async Task<bool> UseRecoveryCodeAsync(WebUserIdentity user, string code, CancellationToken cancellationToken = default)
    {
        var codeHash = HashRecoveryCode(code);
        var isValid = await userRepository.UseRecoveryCodeAsync(user.Id, codeHash, cancellationToken);

        if (!isValid)
        {
            logger.LogWarning("Invalid recovery code attempt for {User}",
                user.ToLogDebug());

            // Audit log for security monitoring (track brute force attempts)
            var actor = user.ToActor();
            await auditLog.LogEventAsync(
                AuditEventType.UserRecoveryCodeVerificationFailed,
                actor: actor,
                target: actor,
                value: "Invalid recovery code entered",
                cancellationToken: cancellationToken);

            return false;
        }

        logger.LogInformation("Recovery code used for {User}",
            user.ToLogInfo());
        return true;
    }

    private static string GenerateRecoveryCode()
    {
        var bytes = RandomNumberGenerator.GetBytes(AuthenticationConstants.RecoveryCodeByteLength);
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
            if (i > 0 && i % AuthenticationConstants.TotpManualEntryGroupSize == 0)
            {
                formatted.Append(' ');
            }
            formatted.Append(secret[i]);
        }
        return formatted.ToString();
    }
}
