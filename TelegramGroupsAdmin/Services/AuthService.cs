using System.Security.Cryptography;
using TelegramGroupsAdmin.Telegram.Repositories.Mappings;
using TelegramGroupsAdmin.Constants;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Core.Utilities;
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

    public async Task<AuthResult> LoginAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetByEmailAsync(email, cancellationToken);
        if (user == null)
        {
            logger.LogWarning("Login attempt for non-existent email: {Email}", email);

            // Audit log - failed login (no user ID available)
            await auditLog.LogEventAsync(
                AuditEventType.UserLoginFailed,
                actor: Actor.FromSystem("login_failed"),
                target: null,
                value: $"Non-existent email: {email}",
                cancellationToken: cancellationToken);

            return new AuthResult(false, null, null, null, false, false, "Invalid email or password");
        }

        // SECURITY-6: Check if account is locked
        if (user.IsLocked)
        {
            var timeRemaining = user.LockedUntil!.Value - DateTimeOffset.UtcNow;
            logger.LogWarning("Login attempt for locked account: {User}, locked until {LockedUntil}",
                user.WebUser.ToLogDebug(), user.LockedUntil);

            // Audit log - failed login (account locked)
            var lockedActor = user.WebUser.ToActor();
            await auditLog.LogEventAsync(
                AuditEventType.UserLoginFailed,
                actor: lockedActor,
                target: lockedActor,
                value: $"Account locked until {user.LockedUntil:yyyy-MM-dd HH:mm:ss UTC}",
                cancellationToken: cancellationToken);

            return new AuthResult(false, null, null, null, false, false,
                $"Account is temporarily locked due to multiple failed login attempts. Please try again in {Math.Ceiling(timeRemaining.TotalMinutes)} minutes.");
        }

        // Check user status (Disabled = 2, Deleted = 3)
        if (user.Status == UserStatus.Disabled)
        {
            logger.LogWarning("Login attempt for disabled account: {User}",
                user.WebUser.ToLogDebug());

            // Audit log - failed login (disabled account)
            var disabledActor = user.WebUser.ToActor();
            await auditLog.LogEventAsync(
                AuditEventType.UserLoginFailed,
                actor: disabledActor,
                target: disabledActor,
                value: "Account disabled",
                cancellationToken: cancellationToken);

            return new AuthResult(false, null, null, null, false, false, "Account has been disabled. Please contact an administrator.");
        }

        if (user.Status == UserStatus.Deleted)
        {
            logger.LogWarning("Login attempt for deleted account: {User}",
                user.WebUser.ToLogDebug());

            // Audit log - failed login (deleted account)
            var deletedActor = user.WebUser.ToActor();
            await auditLog.LogEventAsync(
                AuditEventType.UserLoginFailed,
                actor: deletedActor,
                target: deletedActor,
                value: "Account deleted",
                cancellationToken: cancellationToken);

            return new AuthResult(false, null, null, null, false, false, "Account has been deleted. Please contact an administrator.");
        }

        if (!passwordHasher.VerifyPassword(password, user.PasswordHash))
        {
            logger.LogWarning("Invalid password for {User}",
                user.WebUser.ToLogDebug());

            // Audit log - failed login (wrong password)
            var failedActor = user.WebUser.ToActor();
            await auditLog.LogEventAsync(
                AuditEventType.UserLoginFailed,
                actor: failedActor,
                target: failedActor,
                value: "Invalid password",
                cancellationToken: cancellationToken);

            // SECURITY-6: Handle failed login attempt (may lock account)
            await accountLockoutService.HandleFailedLoginAsync(user.WebUser, cancellationToken);

            return new AuthResult(false, null, null, null, false, false, "Invalid email or password");
        }

        // Check if email is verified
        if (!user.EmailVerified)
        {
            logger.LogWarning("Login attempt for unverified email: {User}",
                user.WebUser.ToLogDebug());

            // Audit log - failed login (email not verified)
            var unverifiedActor = user.WebUser.ToActor();
            await auditLog.LogEventAsync(
                AuditEventType.UserLoginFailed,
                actor: unverifiedActor,
                target: unverifiedActor,
                value: "Email not verified",
                cancellationToken: cancellationToken);

            return new AuthResult(false, null, null, null, false, false, "Please verify your email before logging in. Check your inbox for the verification link.");
        }

        // SECURITY-6: Reset lockout state on successful password verification
        await accountLockoutService.ResetLockoutAsync(user.WebUser, cancellationToken);

        // Update last login timestamp
        await userRepository.UpdateLastLoginAsync(user.WebUser.Id, cancellationToken);

        // Audit log - successful login
        var loginActor = user.WebUser.ToActor();
        await auditLog.LogEventAsync(
            AuditEventType.UserLogin,
            actor: loginActor,
            target: loginActor,
            value: user.TotpEnabled ? "Login (requires TOTP)" : "Login successful",
            cancellationToken: cancellationToken);

        // Handle TOTP states based on enabled flag and secret existence
        if (user.TotpEnabled)
        {
            if (string.IsNullOrEmpty(user.TotpSecret))
            {
                // Admin enabled TOTP but user needs to set it up (forced setup)
                // TotpEnabled=true, RequiresTotp=false → Login.razor redirects to setup
                return new AuthResult(true, user.WebUser.Id, user.WebUser.Email, user.PermissionLevelInt, true, false, null);
            }
            else
            {
                // Normal 2FA verification required
                // TotpEnabled=true, RequiresTotp=true → Login.razor redirects to verify
                return new AuthResult(true, user.WebUser.Id, user.WebUser.Email, user.PermissionLevelInt, true, true, null);
            }
        }

        // TOTP disabled (either never set up or admin disabled for bypass)
        // TotpEnabled=false, RequiresTotp=false → Normal login
        return new AuthResult(true, user.WebUser.Id, user.WebUser.Email, user.PermissionLevelInt, false, false, null);
    }

    public async Task<AuthResult> VerifyTotpAsync(WebUserIdentity user, string code, CancellationToken cancellationToken = default)
    {
        var dbUser = await userRepository.GetByIdAsync(user.Id, cancellationToken);
        if (dbUser is null)
        {
            return new AuthResult(false, null, null, null, false, false, "User not found");
        }

        var isValid = await totpService.VerifyTotpCodeAsync(user, code, cancellationToken);
        if (!isValid)
        {
            return new AuthResult(false, null, null, null, false, false, "Invalid verification code");
        }

        await userRepository.UpdateLastLoginAsync(user.Id, cancellationToken);
        return new AuthResult(true, dbUser.WebUser.Id, dbUser.WebUser.Email, dbUser.PermissionLevelInt, true, false, null);
    }

    public async Task<bool> IsFirstRunAsync(CancellationToken cancellationToken = default)
    {
        return await userRepository.GetUserCountAsync(cancellationToken) == 0;
    }

    public async Task<RegisterResult> RegisterAsync(string email, string password, string? inviteToken, CancellationToken cancellationToken = default)
    {
        // Check if this is first run (no users exist)
        var isFirstRun = await IsFirstRunAsync(cancellationToken);

        if (isFirstRun)
        {
            // First run - create owner account without invite
            return await CreateOwnerAccountAsync(email, password, cancellationToken);
        }

        // Validate invite token for all subsequent users
        var inviteValidation = await ValidateInviteTokenAsync(inviteToken, cancellationToken);
        if (!inviteValidation.IsValid)
        {
            return new RegisterResult(false, null, inviteValidation.ErrorMessage);
        }

        // Check if email is already registered (active/disabled - not deleted)
        var existing = await userRepository.GetByEmailAsync(email, cancellationToken);
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
            cancellationToken);

        logger.LogInformation("User registered: {Email} via invite from {InviterId}", email, inviteValidation.InvitedBy);

        // Audit log
        await auditLog.LogEventAsync(
            AuditEventType.UserRegistered,
            actor: Actor.FromWebUser(userId),
            target: Actor.FromWebUser(userId),
            value: $"Registered via invite from {inviteValidation.InvitedBy}",
            cancellationToken: cancellationToken);

        // Send verification email if email service is configured
        if (await featureAvailability.IsEmailVerificationEnabledAsync())
        {
            await SendVerificationEmailAsync(userId, email, cancellationToken);
        }
        else
        {
            var registeredUser = new WebUserIdentity(userId, email, inviteValidation.PermissionLevel);
            logger.LogWarning(
                "{User} registered without email verification (email service not configured)",
                registeredUser.ToLogDebug());
        }

        return new RegisterResult(true, userId, null);
    }

    /// <summary>
    /// Create the first user (owner) without requiring an invite.
    /// </summary>
    private async Task<RegisterResult> CreateOwnerAccountAsync(string email, string password, CancellationToken cancellationToken)
    {
        logger.LogInformation("First run detected - creating owner account");

        var userId = Guid.NewGuid().ToString();
        var user = new UserRecord(
            WebUser: new WebUserIdentity(userId, email, PermissionLevel.Owner),
            NormalizedEmail: email.ToUpperInvariant(),
            PasswordHash: passwordHasher.HashPassword(password),
            SecurityStamp: Guid.NewGuid().ToString(),
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

        await userRepository.CreateAsync(user, cancellationToken);
        logger.LogInformation("Owner account created: {Email}", email);

        await auditLog.LogEventAsync(
            AuditEventType.UserRegistered,
            actor: Actor.FromWebUser(userId),
            target: Actor.FromWebUser(userId),
            value: "First user (Owner)",
            cancellationToken: cancellationToken);

        return new RegisterResult(true, userId, null);
    }

    private async Task<(bool IsValid, string? ErrorMessage, string? InvitedBy, PermissionLevel PermissionLevel)> ValidateInviteTokenAsync(
        string? inviteToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(inviteToken))
        {
            return (false, "Invite token is required", null, default);
        }

        var invite = await userRepository.GetInviteByTokenAsync(inviteToken, cancellationToken);
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

    public async Task<TotpSetupResult> EnableTotpAsync(WebUserIdentity user, CancellationToken cancellationToken = default)
    {
        var dbUser = await userRepository.GetByIdAsync(user.Id, cancellationToken);
        if (dbUser is null)
        {
            throw new InvalidOperationException("User not found");
        }

        return await totpService.SetupTotpAsync(user, cancellationToken);
    }

    public async Task<TotpVerificationResult> VerifyAndEnableTotpAsync(WebUserIdentity user, string code, CancellationToken cancellationToken = default)
    {
        return await totpService.VerifyAndEnableTotpAsync(user, code, cancellationToken);
    }

    public async Task<bool> AdminDisableTotpAsync(WebUserIdentity target, WebUserIdentity admin, CancellationToken cancellationToken = default)
    {
        return await totpService.AdminDisableTotpAsync(target, admin, cancellationToken);
    }

    public async Task<bool> AdminEnableTotpAsync(WebUserIdentity target, WebUserIdentity admin, CancellationToken cancellationToken = default)
    {
        return await totpService.AdminEnableTotpAsync(target, admin, cancellationToken);
    }

    public async Task<bool> AdminResetTotpAsync(WebUserIdentity target, WebUserIdentity admin, CancellationToken cancellationToken = default)
    {
        return await totpService.AdminResetTotpAsync(target, admin, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GenerateRecoveryCodesAsync(WebUserIdentity user, CancellationToken cancellationToken = default)
    {
        return await totpService.GenerateRecoveryCodesAsync(user, cancellationToken);
    }

    public async Task<AuthResult> UseRecoveryCodeAsync(WebUserIdentity user, string code, CancellationToken cancellationToken = default)
    {
        var dbUser = await userRepository.GetByIdAsync(user.Id, cancellationToken);
        if (dbUser is null)
        {
            return new AuthResult(false, null, null, null, false, false, "Invalid recovery code");
        }

        var isValid = await totpService.UseRecoveryCodeAsync(user, code, cancellationToken);

        if (!isValid)
        {
            return new AuthResult(false, null, null, null, false, false, "Invalid recovery code");
        }

        await userRepository.UpdateLastLoginAsync(user.Id, cancellationToken);

        return new AuthResult(true, dbUser.WebUser.Id, dbUser.WebUser.Email, dbUser.PermissionLevelInt, true, false, null);
    }

    public async Task AuditLogoutAsync(WebUserIdentity user, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("User logged out: {User}", user.ToLogInfo());

        // Audit log
        var actor = user.ToActor();
        await auditLog.LogEventAsync(
            AuditEventType.UserLogout,
            actor: actor,
            target: actor,
            value: "User logged out",
            cancellationToken: cancellationToken);
    }

    public async Task<bool> ChangePasswordAsync(WebUserIdentity user, string currentPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        var dbUser = await userRepository.GetByIdAsync(user.Id, cancellationToken);
        if (dbUser is null)
        {
            logger.LogWarning("Password change attempt for non-existent user: {User}", user.ToLogDebug());
            return false;
        }

        // Verify current password
        if (!passwordHasher.VerifyPassword(currentPassword, dbUser.PasswordHash))
        {
            logger.LogWarning("Invalid current password during password change for {User}",
                user.ToLogDebug());
            return false;
        }

        // Hash new password
        var newPasswordHash = passwordHasher.HashPassword(newPassword);

        // Update password
        var updatedUser = dbUser with
        {
            PasswordHash = newPasswordHash,
            SecurityStamp = Guid.NewGuid().ToString(),
            ModifiedBy = user.Id,
            ModifiedAt = DateTimeOffset.UtcNow
        };

        await userRepository.UpdateAsync(updatedUser, cancellationToken);

        logger.LogInformation("Password changed for {User}", user.ToLogInfo());

        // Audit log
        var actor = user.ToActor();
        await auditLog.LogEventAsync(
            AuditEventType.UserPasswordChanged,
            actor: actor,
            target: actor,
            value: "Password changed",
            cancellationToken: cancellationToken);

        return true;
    }

    private async Task SendVerificationEmailAsync(string userId, string email, CancellationToken cancellationToken)
    {
        try
        {
            // Generate verification token
            var tokenString = Convert.ToBase64String(RandomNumberGenerator.GetBytes(AuthenticationConstants.VerificationTokenByteLength));
            var verificationToken = new VerificationToken(
                Id: 0,
                UserId: userId,
                TokenType: TokenType.EmailVerification,
                Token: tokenString,
                Value: null,
                ExpiresAt: DateTimeOffset.UtcNow.Add(AuthenticationConstants.EmailVerificationTokenExpiration),
                CreatedAt: DateTimeOffset.UtcNow,
                UsedAt: null
            );

            await verificationTokenRepository.CreateAsync(verificationToken.ToDto(), cancellationToken);

            await emailService.SendTemplatedEmailAsync(
                email,
                EmailTemplate.EmailVerification,
                new Dictionary<string, string>
                {
                    { "VerificationToken", tokenString },
                    { "BaseUrl", _appOptions.BaseUrl }
                },
                cancellationToken);

            logger.LogInformation("Sent verification email to {Email}", email);

            // Audit log - email verification sent
            await auditLog.LogEventAsync(
                AuditEventType.UserEmailVerificationSent,
                actor: Actor.FromSystem("email_verification"),
                target: Actor.FromWebUser(userId),
                value: email,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send verification email to {Email}", email);
            // Don't fail registration if email fails
        }
    }

    public async Task<bool> ResendVerificationEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetByEmailAsync(email, cancellationToken);
        if (user == null)
        {
            logger.LogWarning("Resend verification attempt for non-existent email: {Email}", email);
            return false; // Don't reveal if email exists
        }

        // If already verified, no need to resend
        if (user.EmailVerified)
        {
            logger.LogInformation("Resend verification attempt for already verified {User}",
                user.WebUser.ToLogInfo());
            return false;
        }

        // Send verification email using verification_tokens table
        try
        {
            // Generate new verification token
            var tokenString = Convert.ToBase64String(RandomNumberGenerator.GetBytes(AuthenticationConstants.VerificationTokenByteLength));
            var verificationToken = new VerificationToken(
                Id: 0, // Will be set by database
                UserId: user.WebUser.Id,
                TokenType: TokenType.EmailVerification,
                Token: tokenString,
                Value: null,
                ExpiresAt: DateTimeOffset.UtcNow.Add(AuthenticationConstants.EmailVerificationTokenExpiration),
                CreatedAt: DateTimeOffset.UtcNow,
                UsedAt: null
            );

            await verificationTokenRepository.CreateAsync(verificationToken.ToDto(), cancellationToken);

            await emailService.SendTemplatedEmailAsync(
                email,
                EmailTemplate.EmailVerification,
                new Dictionary<string, string>
                {
                    { "VerificationToken", tokenString },
                    { "BaseUrl", _appOptions.BaseUrl }
                },
                cancellationToken);

            logger.LogInformation("Resent verification email to {Email}", email);

            // Audit log
            await auditLog.LogEventAsync(
                AuditEventType.UserEmailVerificationSent,
                actor: Actor.FromSystem("email_verification"), // System event
                target: user.WebUser.ToActor(),
                value: $"Resent to {email}",
                cancellationToken: cancellationToken);

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to resend verification email to {Email}", email);
            return false;
        }
    }

    public async Task<bool> RequestPasswordResetAsync(string email, CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetByEmailAsync(email, cancellationToken);
        if (user is null)
        {
            logger.LogWarning("Password reset requested for non-existent email: {Email}", email);
            return false; // Don't reveal if email exists
        }

        // Generate password reset token
        var tokenString = Convert.ToBase64String(RandomNumberGenerator.GetBytes(AuthenticationConstants.VerificationTokenByteLength));
        var resetToken = new VerificationToken(
            Id: 0, // Will be set by database
            UserId: user.WebUser.Id,
            TokenType: TokenType.PasswordReset,
            Token: tokenString,
            Value: null,
            ExpiresAt: DateTimeOffset.UtcNow.Add(AuthenticationConstants.PasswordResetTokenExpiration),
            CreatedAt: DateTimeOffset.UtcNow,
            UsedAt: null
        );

        await verificationTokenRepository.CreateAsync(resetToken.ToDto(), cancellationToken);

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
                cancellationToken);

            logger.LogInformation("Password reset email sent to {Email}", email);

            // Audit log
            await auditLog.LogEventAsync(
                AuditEventType.UserPasswordResetRequested,
                actor: Actor.FromSystem("password_reset"), // System event
                target: user.WebUser.ToActor(),
                value: email,
                cancellationToken: cancellationToken);

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send password reset email to {Email}", email);
            return false;
        }
    }

    public async Task<bool> ResetPasswordAsync(string token, string newPassword, CancellationToken cancellationToken = default)
    {
        // Validate token
        var resetToken = await verificationTokenRepository.GetValidTokenAsync(token, (DataModels.TokenType)TokenType.PasswordReset, cancellationToken);
        if (resetToken is null)
        {
            logger.LogWarning("Invalid or expired password reset token attempted");
            return false;
        }

        // Get user
        var user = await userRepository.GetByIdAsync(resetToken.UserId, cancellationToken);
        if (user is null)
        {
            logger.LogWarning("Password reset token references non-existent user {UserId}", resetToken.UserId);
            // No email available - just log the ID
            return false;
        }

        // Update password
        var newPasswordHash = passwordHasher.HashPassword(newPassword);
        var updatedUser = user with
        {
            PasswordHash = newPasswordHash,
            SecurityStamp = Guid.NewGuid().ToString(),
            ModifiedBy = user.WebUser.Id,
            ModifiedAt = DateTimeOffset.UtcNow
        };

        await userRepository.UpdateAsync(updatedUser, cancellationToken);

        // Mark token as used
        await verificationTokenRepository.MarkAsUsedAsync(token, cancellationToken);

        logger.LogInformation("Password reset for {User}", user.WebUser.ToLogInfo());

        // Audit log
        var actor = user.WebUser.ToActor();
        await auditLog.LogEventAsync(
            AuditEventType.UserPasswordChanged,
            actor: actor,
            target: actor,
            value: "Password reset via email",
            cancellationToken: cancellationToken);

        return true;
    }
}
