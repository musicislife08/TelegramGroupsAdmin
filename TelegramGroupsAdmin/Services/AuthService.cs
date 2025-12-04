using System.Security.Cryptography;
using TelegramGroupsAdmin.Telegram.Repositories.Mappings;
using TelegramGroupsAdmin.Core.Models;
using Microsoft.Extensions.Options;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Repositories;
using TelegramGroupsAdmin.Services.Auth;
using TelegramGroupsAdmin.Services.Email;
using DataModels = TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Services;

public class AuthService(
    IUserRepository userRepository,
    IVerificationTokenRepository verificationTokenRepository,
    IAuditService auditLog,
    ITotpService totpService,
    IPasswordHasher passwordHasher,
    IEmailService emailService,
    IAccountLockoutService accountLockoutService,
    IFeatureAvailabilityService featureAvailability,
    IOptions<AppOptions> appOptions,
    ILogger<AuthService> logger)
    : IAuthService
{
    private readonly AppOptions _appOptions = appOptions.Value;

    public async Task<AuthResult> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        var user = await userRepository.GetByEmailAsync(email, ct);
        if (user == null)
        {
            logger.LogWarning("Login attempt for non-existent email: {Email}", email);

            // Audit log - failed login (no user ID available)
            await auditLog.LogEventAsync(
                AuditEventType.UserLoginFailed,
                actor: Actor.FromSystem("login_failed"),
                target: null,
                value: $"Non-existent email: {email}",
                ct: ct);

            return new AuthResult(false, null, null, null, false, false, "Invalid email or password");
        }

        // SECURITY-6: Check if account is locked
        if (user.IsLocked)
        {
            var timeRemaining = user.LockedUntil!.Value - DateTimeOffset.UtcNow;
            logger.LogWarning("Login attempt for locked account: {UserId}, locked until {LockedUntil}", user.Id, user.LockedUntil);

            // Audit log - failed login (account locked)
            await auditLog.LogEventAsync(
                AuditEventType.UserLoginFailed,
                actor: Actor.FromWebUser(user.Id),
                target: Actor.FromWebUser(user.Id),
                value: $"Account locked until {user.LockedUntil:yyyy-MM-dd HH:mm:ss UTC}",
                ct: ct);

            return new AuthResult(false, null, null, null, false, false,
                $"Account is temporarily locked due to multiple failed login attempts. Please try again in {Math.Ceiling(timeRemaining.TotalMinutes)} minutes.");
        }

        // Check user status (Disabled = 2, Deleted = 3)
        if (user.Status == UserStatus.Disabled)
        {
            logger.LogWarning("Login attempt for disabled user: {UserId}", user.Id);

            // Audit log - failed login (disabled account)
            await auditLog.LogEventAsync(
                AuditEventType.UserLoginFailed,
                actor: Actor.FromWebUser(user.Id),
                target: Actor.FromWebUser(user.Id),
                value: "Account disabled",
                ct: ct);

            return new AuthResult(false, null, null, null, false, false, "Account has been disabled. Please contact an administrator.");
        }

        if (user.Status == UserStatus.Deleted)
        {
            logger.LogWarning("Login attempt for deleted user: {UserId}", user.Id);

            // Audit log - failed login (deleted account)
            await auditLog.LogEventAsync(
                AuditEventType.UserLoginFailed,
                actor: Actor.FromWebUser(user.Id),
                target: Actor.FromWebUser(user.Id),
                value: "Account deleted",
                ct: ct);

            return new AuthResult(false, null, null, null, false, false, "Account has been deleted. Please contact an administrator.");
        }

        if (!passwordHasher.VerifyPassword(password, user.PasswordHash))
        {
            logger.LogWarning("Invalid password for user: {UserId}", user.Id);

            // Audit log - failed login (wrong password)
            await auditLog.LogEventAsync(
                AuditEventType.UserLoginFailed,
                actor: Actor.FromWebUser(user.Id),
                target: Actor.FromWebUser(user.Id),
                value: "Invalid password",
                ct: ct);

            // SECURITY-6: Handle failed login attempt (may lock account)
            await accountLockoutService.HandleFailedLoginAsync(user.Id, ct);

            return new AuthResult(false, null, null, null, false, false, "Invalid email or password");
        }

        // Check if email is verified
        if (!user.EmailVerified)
        {
            logger.LogWarning("Login attempt for unverified email: {UserId}", user.Id);

            // Audit log - failed login (email not verified)
            await auditLog.LogEventAsync(
                AuditEventType.UserLoginFailed,
                actor: Actor.FromWebUser(user.Id),
                target: Actor.FromWebUser(user.Id),
                value: "Email not verified",
                ct: ct);

            return new AuthResult(false, null, null, null, false, false, "Please verify your email before logging in. Check your inbox for the verification link.");
        }

        // SECURITY-6: Reset lockout state on successful password verification
        await accountLockoutService.ResetLockoutAsync(user.Id, ct);

        // Update last login timestamp
        await userRepository.UpdateLastLoginAsync(user.Id, ct);

        // Audit log - successful login
        await auditLog.LogEventAsync(
            AuditEventType.UserLogin,
            actor: Actor.FromWebUser(user.Id),
            target: Actor.FromWebUser(user.Id),
            value: user.TotpEnabled ? "Login (requires TOTP)" : "Login successful",
            ct: ct);

        // Handle TOTP states based on enabled flag and secret existence
        if (user.TotpEnabled)
        {
            if (string.IsNullOrEmpty(user.TotpSecret))
            {
                // Admin enabled TOTP but user needs to set it up (forced setup)
                // TotpEnabled=true, RequiresTotp=false → Login.razor redirects to setup
                return new AuthResult(true, user.Id, user.Email, user.PermissionLevelInt, true, false, null);
            }
            else
            {
                // Normal 2FA verification required
                // TotpEnabled=true, RequiresTotp=true → Login.razor redirects to verify
                return new AuthResult(true, user.Id, user.Email, user.PermissionLevelInt, true, true, null);
            }
        }

        // TOTP disabled (either never set up or admin disabled for bypass)
        // TotpEnabled=false, RequiresTotp=false → Normal login
        return new AuthResult(true, user.Id, user.Email, user.PermissionLevelInt, false, false, null);
    }

    public async Task<AuthResult> VerifyTotpAsync(string userId, string code, CancellationToken ct = default)
    {
        var user = await userRepository.GetByIdAsync(userId, ct);
        if (user is null)
        {
            return new AuthResult(false, null, null, null, false, false, "User not found");
        }

        var isValid = await totpService.VerifyTotpCodeAsync(userId, code, ct);
        if (!isValid)
        {
            return new AuthResult(false, null, null, null, false, false, "Invalid verification code");
        }

        await userRepository.UpdateLastLoginAsync(userId, ct);
        return new AuthResult(true, user.Id, user.Email, user.PermissionLevelInt, true, false, null);
    }

    public async Task<bool> IsFirstRunAsync(CancellationToken ct = default)
    {
        return await userRepository.GetUserCountAsync(ct) == 0;
    }

    public async Task<RegisterResult> RegisterAsync(string email, string password, string? inviteToken, CancellationToken ct = default)
    {
        // Check if this is first run (no users exist)
        var isFirstRun = await IsFirstRunAsync(ct);

        if (isFirstRun)
        {
            // First run - create owner account without invite
            return await CreateOwnerAccountAsync(email, password, ct);
        }

        // Validate invite token for all subsequent users
        var inviteValidation = await ValidateInviteTokenAsync(inviteToken, ct);
        if (!inviteValidation.IsValid)
        {
            return new RegisterResult(false, null, inviteValidation.ErrorMessage);
        }

        // Check if email is already registered (active/disabled - not deleted)
        var existing = await userRepository.GetByEmailAsync(email, ct);
        if (existing != null)
        {
            logger.LogWarning("Registration attempt for existing active/disabled user: {Email}", email);
            return new RegisterResult(false, null, "Email already registered");
        }

        // Atomic: register user (create or reactivate) + mark invite as used
        var passwordHash = passwordHasher.HashPassword(password);
        var userId = await userRepository.RegisterUserWithInviteAsync(
            email,
            passwordHash,
            inviteValidation.PermissionLevel,
            inviteValidation.InvitedBy,
            inviteToken!,
            ct);

        logger.LogInformation("User registered: {UserId} via invite from {InviterId}", userId, inviteValidation.InvitedBy);

        // Audit log
        await auditLog.LogEventAsync(
            AuditEventType.UserRegistered,
            actor: Actor.FromWebUser(userId),
            target: Actor.FromWebUser(userId),
            value: $"Registered via invite from {inviteValidation.InvitedBy}",
            ct: ct);

        // Send verification email if email service is configured
        if (await featureAvailability.IsEmailVerificationEnabledAsync())
        {
            await SendVerificationEmailAsync(userId, email, ct);
        }
        else
        {
            logger.LogWarning(
                "User {UserId} registered without email verification (email service not configured)",
                userId);
        }

        return new RegisterResult(true, userId, null);
    }

    /// <summary>
    /// Create the first user (owner) without requiring an invite.
    /// </summary>
    private async Task<RegisterResult> CreateOwnerAccountAsync(string email, string password, CancellationToken ct)
    {
        logger.LogInformation("First run detected - creating owner account");

        var userId = Guid.NewGuid().ToString();
        var user = new UserRecord(
            Id: userId,
            Email: email,
            NormalizedEmail: email.ToUpperInvariant(),
            PasswordHash: passwordHasher.HashPassword(password),
            SecurityStamp: Guid.NewGuid().ToString(),
            PermissionLevel: PermissionLevel.Owner,
            InvitedBy: null,
            IsActive: true,
            TotpSecret: null,
            TotpEnabled: true, // All users must set up 2FA by default (owners can disable if needed)
            TotpSetupStartedAt: null,
            CreatedAt: DateTimeOffset.UtcNow,
            LastLoginAt: null,
            Status: UserStatus.Active,
            ModifiedBy: null,
            ModifiedAt: null,
            EmailVerified: !await featureAvailability.IsEmailVerificationEnabledAsync(), // Skip verification if email not configured
            EmailVerificationToken: null,
            EmailVerificationTokenExpiresAt: null,
            PasswordResetToken: null,
            PasswordResetTokenExpiresAt: null,
            FailedLoginAttempts: 0,
            LockedUntil: null
        );

        await userRepository.CreateAsync(user, ct);
        logger.LogInformation("Owner account created: {UserId}", userId);

        await auditLog.LogEventAsync(
            AuditEventType.UserRegistered,
            actor: Actor.FromWebUser(userId),
            target: Actor.FromWebUser(userId),
            value: "First user (Owner)",
            ct: ct);

        return new RegisterResult(true, userId, null);
    }

    private async Task<(bool IsValid, string? ErrorMessage, string? InvitedBy, PermissionLevel PermissionLevel)> ValidateInviteTokenAsync(
        string? inviteToken,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(inviteToken))
        {
            return (false, "Invite token is required", null, default);
        }

        var invite = await userRepository.GetInviteByTokenAsync(inviteToken, ct);
        if (invite == null)
        {
            return (false, "Invalid invite token", null, default);
        }

        switch (invite.Status)
        {
            case InviteStatus.Used:
                return (false, "Invite token already used", null, default);
            case InviteStatus.Revoked:
                return (false, "Invite token has been revoked", null, default);
            case InviteStatus.Pending:
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        if (invite.ExpiresAt < DateTimeOffset.UtcNow)
        {
            return (false, "Invite token expired", null, default);
        }

        return (true, null, invite.CreatedBy, invite.PermissionLevel);
    }

    public async Task<TotpSetupResult> EnableTotpAsync(string userId, CancellationToken ct = default)
    {
        var user = await userRepository.GetByIdAsync(userId, ct);
        if (user is null)
        {
            throw new InvalidOperationException("User not found");
        }

        return await totpService.SetupTotpAsync(userId, user.Email, ct);
    }

    public async Task<TotpVerificationResult> VerifyAndEnableTotpAsync(string userId, string code, CancellationToken ct = default)
    {
        return await totpService.VerifyAndEnableTotpAsync(userId, code, ct);
    }

    public async Task<bool> DisableTotpAsync(string userId, string password, CancellationToken ct = default)
    {
        return await totpService.DisableTotpAsync(userId, password, ct);
    }

    public async Task<bool> AdminDisableTotpAsync(string targetUserId, string adminUserId, CancellationToken ct = default)
    {
        return await totpService.AdminDisableTotpAsync(targetUserId, adminUserId, ct);
    }

    public async Task<bool> AdminEnableTotpAsync(string targetUserId, string adminUserId, CancellationToken ct = default)
    {
        return await totpService.AdminEnableTotpAsync(targetUserId, adminUserId, ct);
    }

    public async Task<bool> AdminResetTotpAsync(string targetUserId, string adminUserId, CancellationToken ct = default)
    {
        return await totpService.AdminResetTotpAsync(targetUserId, adminUserId, ct);
    }

    public async Task<IReadOnlyList<string>> GenerateRecoveryCodesAsync(string userId, CancellationToken ct = default)
    {
        return await totpService.GenerateRecoveryCodesAsync(userId, ct);
    }

    public async Task<AuthResult> UseRecoveryCodeAsync(string userId, string code, CancellationToken ct = default)
    {
        var user = await userRepository.GetByIdAsync(userId, ct);
        if (user is null)
        {
            return new AuthResult(false, null, null, null, false, false, "Invalid recovery code");
        }

        var isValid = await totpService.UseRecoveryCodeAsync(userId, code, ct);

        if (!isValid)
        {
            return new AuthResult(false, null, null, null, false, false, "Invalid recovery code");
        }

        await userRepository.UpdateLastLoginAsync(userId, ct);

        return new AuthResult(true, user.Id, user.Email, user.PermissionLevelInt, true, false, null);
    }

    public async Task LogoutAsync(string userId, CancellationToken ct = default)
    {
        logger.LogInformation("User logged out: {UserId}", userId);

        // Audit log
        await auditLog.LogEventAsync(
            AuditEventType.UserLogout,
            actor: Actor.FromWebUser(userId),
            target: Actor.FromWebUser(userId),
            value: "User logged out",
            ct: ct);
    }

    public async Task<bool> ChangePasswordAsync(string userId, string currentPassword, string newPassword, CancellationToken ct = default)
    {
        var user = await userRepository.GetByIdAsync(userId, ct);
        if (user is null)
        {
            logger.LogWarning("Password change attempt for non-existent user: {UserId}", userId);
            return false;
        }

        // Verify current password
        if (!passwordHasher.VerifyPassword(currentPassword, user.PasswordHash))
        {
            logger.LogWarning("Invalid current password during password change for user: {UserId}", userId);
            return false;
        }

        // Hash new password
        var newPasswordHash = passwordHasher.HashPassword(newPassword);

        // Update password
        var updatedUser = user with
        {
            PasswordHash = newPasswordHash,
            SecurityStamp = Guid.NewGuid().ToString(),
            ModifiedBy = userId,
            ModifiedAt = DateTimeOffset.UtcNow
        };

        await userRepository.UpdateAsync(updatedUser, ct);

        logger.LogInformation("Password changed for user: {UserId}", userId);

        // Audit log
        await auditLog.LogEventAsync(
            AuditEventType.UserPasswordChanged,
            actor: Actor.FromWebUser(userId),
            target: Actor.FromWebUser(userId),
            value: "Password changed",
            ct: ct);

        return true;
    }

    private async Task SendVerificationEmailAsync(string userId, string email, CancellationToken ct)
    {
        try
        {
            // Generate verification token
            var tokenString = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            var verificationToken = new VerificationToken(
                Id: 0,
                UserId: userId,
                TokenType: TokenType.EmailVerification,
                Token: tokenString,
                Value: null,
                ExpiresAt: DateTimeOffset.UtcNow.AddHours(24),
                CreatedAt: DateTimeOffset.UtcNow,
                UsedAt: null
            );

            await verificationTokenRepository.CreateAsync(verificationToken.ToDto(), ct);

            await emailService.SendTemplatedEmailAsync(
                email,
                EmailTemplate.EmailVerification,
                new Dictionary<string, string>
                {
                    { "VerificationToken", tokenString },
                    { "BaseUrl", _appOptions.BaseUrl }
                },
                ct);

            logger.LogInformation("Sent verification email to {Email}", email);

            // Audit log - email verification sent
            await auditLog.LogEventAsync(
                AuditEventType.UserEmailVerificationSent,
                actor: Actor.FromSystem("email_verification"),
                target: Actor.FromWebUser(userId),
                value: email,
                ct: ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send verification email to {Email}", email);
            // Don't fail registration if email fails
        }
    }

    public async Task<bool> ResendVerificationEmailAsync(string email, CancellationToken ct = default)
    {
        var user = await userRepository.GetByEmailAsync(email, ct);
        if (user == null)
        {
            logger.LogWarning("Resend verification attempt for non-existent email: {Email}", email);
            return false; // Don't reveal if email exists
        }

        // If already verified, no need to resend
        if (user.EmailVerified)
        {
            logger.LogInformation("Resend verification attempt for already verified user: {UserId}", user.Id);
            return false;
        }

        // Send verification email using verification_tokens table
        try
        {
            // Generate new verification token
            var tokenString = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            var verificationToken = new VerificationToken(
                Id: 0, // Will be set by database
                UserId: user.Id,
                TokenType: TokenType.EmailVerification,
                Token: tokenString,
                Value: null,
                ExpiresAt: DateTimeOffset.UtcNow.AddHours(24),
                CreatedAt: DateTimeOffset.UtcNow,
                UsedAt: null
            );

            await verificationTokenRepository.CreateAsync(verificationToken.ToDto(), ct);

            await emailService.SendTemplatedEmailAsync(
                email,
                EmailTemplate.EmailVerification,
                new Dictionary<string, string>
                {
                    { "VerificationToken", tokenString },
                    { "BaseUrl", _appOptions.BaseUrl }
                },
                ct);

            logger.LogInformation("Resent verification email to {Email}", email);

            // Audit log
            await auditLog.LogEventAsync(
                AuditEventType.UserEmailVerificationSent,
                actor: Actor.FromSystem("email_verification"), // System event
                target: Actor.FromWebUser(user.Id),
                value: $"Resent to {email}",
                ct: ct);

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to resend verification email to {Email}", email);
            return false;
        }
    }

    public async Task<bool> RequestPasswordResetAsync(string email, CancellationToken ct = default)
    {
        var user = await userRepository.GetByEmailAsync(email, ct);
        if (user is null)
        {
            logger.LogWarning("Password reset requested for non-existent email: {Email}", email);
            return false; // Don't reveal if email exists
        }

        // Generate password reset token
        var tokenString = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var resetToken = new VerificationToken(
            Id: 0, // Will be set by database
            UserId: user.Id,
            TokenType: TokenType.PasswordReset,
            Token: tokenString,
            Value: null,
            ExpiresAt: DateTimeOffset.UtcNow.AddHours(1), // 1 hour expiry for password reset
            CreatedAt: DateTimeOffset.UtcNow,
            UsedAt: null
        );

        await verificationTokenRepository.CreateAsync(resetToken.ToDto(), ct);

        // Send password reset email
        try
        {
            var resetLink = $"{_appOptions.BaseUrl}/reset-password?token={Uri.EscapeDataString(tokenString)}";

            await emailService.SendTemplatedEmailAsync(
                email,
                EmailTemplate.PasswordReset,
                new Dictionary<string, string>
                {
                    { "resetLink", resetLink },
                    { "expiryMinutes", "60" }
                },
                ct);

            logger.LogInformation("Password reset email sent to {Email}", email);

            // Audit log
            await auditLog.LogEventAsync(
                AuditEventType.UserPasswordResetRequested,
                actor: Actor.FromSystem("password_reset"), // System event
                target: Actor.FromWebUser(user.Id),
                value: email,
                ct: ct);

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send password reset email to {Email}", email);
            return false;
        }
    }

    public async Task<bool> ResetPasswordAsync(string token, string newPassword, CancellationToken ct = default)
    {
        // Validate token
        var resetToken = await verificationTokenRepository.GetValidTokenAsync(token, (DataModels.TokenType)TokenType.PasswordReset, ct);
        if (resetToken is null)
        {
            logger.LogWarning("Invalid or expired password reset token attempted");
            return false;
        }

        // Get user
        var user = await userRepository.GetByIdAsync(resetToken.UserId, ct);
        if (user is null)
        {
            logger.LogWarning("Password reset token references non-existent user {UserId}", resetToken.UserId);
            return false;
        }

        // Update password
        var newPasswordHash = passwordHasher.HashPassword(newPassword);
        var updatedUser = user with
        {
            PasswordHash = newPasswordHash,
            SecurityStamp = Guid.NewGuid().ToString(),
            ModifiedBy = user.Id,
            ModifiedAt = DateTimeOffset.UtcNow
        };

        await userRepository.UpdateAsync(updatedUser, ct);

        // Mark token as used
        await verificationTokenRepository.MarkAsUsedAsync(token, ct);

        logger.LogInformation("Password reset for user {UserId}", user.Id);

        // Audit log
        await auditLog.LogEventAsync(
            AuditEventType.UserPasswordChanged,
            actor: Actor.FromWebUser(user.Id),
            target: Actor.FromWebUser(user.Id),
            value: "Password reset via email",
            ct: ct);

        return true;
    }
}
