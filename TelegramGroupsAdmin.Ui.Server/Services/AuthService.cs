using System.Security.Cryptography;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Telegram.Repositories.Mappings;
using TelegramGroupsAdmin.Ui.Server.Constants;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;
using Microsoft.Extensions.Options;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Ui.Server.Repositories;
using TelegramGroupsAdmin.Ui.Server.Services.Auth;
using TelegramGroupsAdmin.Ui.Server.Services.Email;
using DataModels = TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Ui.Server.Services;

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

            return new AuthResult(
                Success: false,
                UserId: null,
                Email: null,
                PermissionLevel: null,
                SecurityStamp: null,
                TotpEnabled: false,
                RequiresTotp: false,
                ErrorMessage: "Invalid email or password");
        }

        // SECURITY-6: Check if account is locked
        if (user.IsLocked)
        {
            var timeRemaining = user.LockedUntil!.Value - DateTimeOffset.UtcNow;
            logger.LogWarning("Login attempt for locked account: {User}, locked until {LockedUntil}",
                LogDisplayName.WebUserDebug(user.Email, user.Id), user.LockedUntil);

            // Audit log - failed login (account locked)
            await auditLog.LogEventAsync(
                AuditEventType.UserLoginFailed,
                actor: Actor.FromWebUser(user.Id),
                target: Actor.FromWebUser(user.Id),
                value: $"Account locked until {user.LockedUntil:yyyy-MM-dd HH:mm:ss UTC}",
                cancellationToken: cancellationToken);

            return new AuthResult(
                Success: false,
                UserId: null,
                Email: null,
                PermissionLevel: null,
                SecurityStamp: null,
                TotpEnabled: false,
                RequiresTotp: false,
                ErrorMessage: $"Account is temporarily locked due to multiple failed login attempts. Please try again in {Math.Ceiling(timeRemaining.TotalMinutes)} minutes.");
        }

        // Check user status (Disabled = 2, Deleted = 3)
        if (user.Status == UserStatus.Disabled)
        {
            logger.LogWarning("Login attempt for disabled account: {User}",
                LogDisplayName.WebUserDebug(user.Email, user.Id));

            // Audit log - failed login (disabled account)
            await auditLog.LogEventAsync(
                AuditEventType.UserLoginFailed,
                actor: Actor.FromWebUser(user.Id),
                target: Actor.FromWebUser(user.Id),
                value: "Account disabled",
                cancellationToken: cancellationToken);

            return new AuthResult(
                Success: false,
                UserId: null,
                Email: null,
                PermissionLevel: null,
                SecurityStamp: null,
                TotpEnabled: false,
                RequiresTotp: false,
                ErrorMessage: "Account has been disabled. Please contact an administrator.");
        }

        if (user.Status == UserStatus.Deleted)
        {
            logger.LogWarning("Login attempt for deleted account: {User}",
                LogDisplayName.WebUserDebug(user.Email, user.Id));

            // Audit log - failed login (deleted account)
            await auditLog.LogEventAsync(
                AuditEventType.UserLoginFailed,
                actor: Actor.FromWebUser(user.Id),
                target: Actor.FromWebUser(user.Id),
                value: "Account deleted",
                cancellationToken: cancellationToken);

            return new AuthResult(
                Success: false,
                UserId: null,
                Email: null,
                PermissionLevel: null,
                SecurityStamp: null,
                TotpEnabled: false,
                RequiresTotp: false,
                ErrorMessage: "Account has been deleted. Please contact an administrator.");
        }

        if (!passwordHasher.VerifyPassword(password, user.PasswordHash))
        {
            logger.LogWarning("Invalid password for {User}",
                LogDisplayName.WebUserDebug(user.Email, user.Id));

            // Audit log - failed login (wrong password)
            await auditLog.LogEventAsync(
                AuditEventType.UserLoginFailed,
                actor: Actor.FromWebUser(user.Id),
                target: Actor.FromWebUser(user.Id),
                value: "Invalid password",
                cancellationToken: cancellationToken);

            // SECURITY-6: Handle failed login attempt (may lock account)
            await accountLockoutService.HandleFailedLoginAsync(user.Id, cancellationToken);

            return new AuthResult(
                Success: false,
                UserId: null,
                Email: null,
                PermissionLevel: null,
                SecurityStamp: null,
                TotpEnabled: false,
                RequiresTotp: false,
                ErrorMessage: "Invalid email or password");
        }

        // Check if email is verified
        if (!user.EmailVerified)
        {
            logger.LogWarning("Login attempt for unverified email: {User}",
                LogDisplayName.WebUserDebug(user.Email, user.Id));

            // Audit log - failed login (email not verified)
            await auditLog.LogEventAsync(
                AuditEventType.UserLoginFailed,
                actor: Actor.FromWebUser(user.Id),
                target: Actor.FromWebUser(user.Id),
                value: "Email not verified",
                cancellationToken: cancellationToken);

            return new AuthResult(
                Success: false,
                UserId: null,
                Email: null,
                PermissionLevel: null,
                SecurityStamp: null,
                TotpEnabled: false,
                RequiresTotp: false,
                ErrorMessage: "Please verify your email before logging in. Check your inbox for the verification link.");
        }

        // SECURITY-6: Reset lockout state on successful password verification
        await accountLockoutService.ResetLockoutAsync(user.Id, cancellationToken);

        // Update last login timestamp
        await userRepository.UpdateLastLoginAsync(user.Id, cancellationToken);

        // Audit log - successful login
        await auditLog.LogEventAsync(
            AuditEventType.UserLogin,
            actor: Actor.FromWebUser(user.Id),
            target: Actor.FromWebUser(user.Id),
            value: user.TotpEnabled ? "Login (requires TOTP)" : "Login successful",
            cancellationToken: cancellationToken);

        // Handle TOTP states based on enabled flag and secret existence
        if (user.TotpEnabled)
        {
            if (string.IsNullOrEmpty(user.TotpSecret))
            {
                // Admin enabled TOTP but user needs to set it up (forced setup)
                // TotpEnabled=true, RequiresTotp=false → Login.razor redirects to setup
                return new AuthResult(
                    Success: true,
                    UserId: user.Id,
                    Email: user.Email,
                    PermissionLevel: user.PermissionLevelInt,
                    SecurityStamp: user.SecurityStamp,
                    TotpEnabled: true,
                    RequiresTotp: false,
                    ErrorMessage: null);
            }
            else
            {
                // Normal 2FA verification required
                // TotpEnabled=true, RequiresTotp=true → Login.razor redirects to verify
                return new AuthResult(
                    Success: true,
                    UserId: user.Id,
                    Email: user.Email,
                    PermissionLevel: user.PermissionLevelInt,
                    SecurityStamp: user.SecurityStamp,
                    TotpEnabled: true,
                    RequiresTotp: true,
                    ErrorMessage: null);
            }
        }

        // TOTP disabled (either never set up or admin disabled for bypass)
        // TotpEnabled=false, RequiresTotp=false → Normal login
        return new AuthResult(
            Success: true,
            UserId: user.Id,
            Email: user.Email,
            PermissionLevel: user.PermissionLevelInt,
            SecurityStamp: user.SecurityStamp,
            TotpEnabled: false,
            RequiresTotp: false,
            ErrorMessage: null);
    }

    public async Task<AuthResult> VerifyTotpAsync(string userId, string code, CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return new AuthResult(
                Success: false,
                UserId: null,
                Email: null,
                PermissionLevel: null,
                SecurityStamp: null,
                TotpEnabled: false,
                RequiresTotp: false,
                ErrorMessage: "User not found");
        }

        var isValid = await totpService.VerifyTotpCodeAsync(userId, code, cancellationToken);
        if (!isValid)
        {
            return new AuthResult(
                Success: false,
                UserId: null,
                Email: null,
                PermissionLevel: null,
                SecurityStamp: null,
                TotpEnabled: false,
                RequiresTotp: false,
                ErrorMessage: "Invalid verification code");
        }

        await userRepository.UpdateLastLoginAsync(userId, cancellationToken);
        return new AuthResult(
            Success: true,
            UserId: user.Id,
            Email: user.Email,
            PermissionLevel: user.PermissionLevelInt,
            SecurityStamp: user.SecurityStamp,
            TotpEnabled: true,
            RequiresTotp: false,
            ErrorMessage: null);
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
            logger.LogWarning(
                "{User} registered without email verification (email service not configured)",
                LogDisplayName.WebUserDebug(email, userId));
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
        if (invite is null)
        {
            return (false, "Invalid invite token", null, default);
        }

        // Validate invite status
        var statusError = invite.Status switch
        {
            InviteStatus.Used => "Invite token already used",
            InviteStatus.Revoked => "Invite token has been revoked",
            InviteStatus.Pending => null,
            _ => throw new ArgumentOutOfRangeException(nameof(invite.Status), invite.Status, "Unknown invite status")
        };

        if (statusError is not null)
        {
            return (false, statusError, null, default);
        }

        if (invite.ExpiresAt < DateTimeOffset.UtcNow)
        {
            return (false, "Invite token expired", null, default);
        }

        return (true, null, invite.CreatedBy, invite.PermissionLevel);
    }

    public async Task<TotpSetupResult> EnableTotpAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            throw new InvalidOperationException("User not found");
        }

        return await totpService.SetupTotpAsync(userId, user.Email, cancellationToken);
    }

    public async Task<TotpVerificationResult> VerifyAndEnableTotpAsync(string userId, string code, CancellationToken cancellationToken = default)
    {
        var result = await totpService.VerifyAndEnableTotpAsync(userId, code, cancellationToken);

        if (!result.Success)
        {
            return result;
        }

        // Fetch user info to include in result for authentication
        var user = await userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return new TotpVerificationResult(false, false, "User not found");
        }

        return result with
        {
            UserId = user.Id,
            Email = user.Email,
            PermissionLevel = user.PermissionLevelInt,
            SecurityStamp = user.SecurityStamp
        };
    }

    public async Task<bool> AdminDisableTotpAsync(string targetUserId, string adminUserId, CancellationToken cancellationToken = default)
    {
        return await totpService.AdminDisableTotpAsync(targetUserId, adminUserId, cancellationToken);
    }

    public async Task<bool> AdminEnableTotpAsync(string targetUserId, string adminUserId, CancellationToken cancellationToken = default)
    {
        return await totpService.AdminEnableTotpAsync(targetUserId, adminUserId, cancellationToken);
    }

    public async Task<bool> AdminResetTotpAsync(string targetUserId, string adminUserId, CancellationToken cancellationToken = default)
    {
        return await totpService.AdminResetTotpAsync(targetUserId, adminUserId, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GenerateRecoveryCodesAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await totpService.GenerateRecoveryCodesAsync(userId, cancellationToken);
    }

    public async Task<AuthResult> UseRecoveryCodeAsync(string userId, string code, CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return new AuthResult(
                Success: false,
                UserId: null,
                Email: null,
                PermissionLevel: null,
                SecurityStamp: null,
                TotpEnabled: false,
                RequiresTotp: false,
                ErrorMessage: "Invalid recovery code");
        }

        var isValid = await totpService.UseRecoveryCodeAsync(userId, code, cancellationToken);

        if (!isValid)
        {
            return new AuthResult(
                Success: false,
                UserId: null,
                Email: null,
                PermissionLevel: null,
                SecurityStamp: null,
                TotpEnabled: false,
                RequiresTotp: false,
                ErrorMessage: "Invalid recovery code");
        }

        await userRepository.UpdateLastLoginAsync(userId, cancellationToken);

        return new AuthResult(
            Success: true,
            UserId: user.Id,
            Email: user.Email,
            PermissionLevel: user.PermissionLevelInt,
            SecurityStamp: user.SecurityStamp,
            TotpEnabled: true,
            RequiresTotp: false,
            ErrorMessage: null);
    }

    public async Task LogoutAsync(string userId, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("User logged out: {UserId}", userId); // Note: No email in scope

        // Audit log
        await auditLog.LogEventAsync(
            AuditEventType.UserLogout,
            actor: Actor.FromWebUser(userId),
            target: Actor.FromWebUser(userId),
            value: "User logged out",
            cancellationToken: cancellationToken);
    }

    public async Task<bool> ChangePasswordAsync(string userId, string currentPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            logger.LogWarning("Password change attempt for non-existent user: {UserId}", userId);
            return false;
        }

        // Verify current password
        if (!passwordHasher.VerifyPassword(currentPassword, user.PasswordHash))
        {
            logger.LogWarning("Invalid current password during password change for {User}",
                LogDisplayName.WebUserDebug(user.Email, userId));
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

        await userRepository.UpdateAsync(updatedUser, cancellationToken);

        logger.LogInformation("Password changed for {User}", LogDisplayName.WebUserInfo(user.Email, userId));

        // Audit log
        await auditLog.LogEventAsync(
            AuditEventType.UserPasswordChanged,
            actor: Actor.FromWebUser(userId),
            target: Actor.FromWebUser(userId),
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
                new EmailTemplateData.EmailVerification(tokenString, _appOptions.BaseUrl),
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
                LogDisplayName.WebUserInfo(email, user.Id));
            return false;
        }

        // Send verification email using verification_tokens table
        try
        {
            // Generate new verification token
            var tokenString = Convert.ToBase64String(RandomNumberGenerator.GetBytes(AuthenticationConstants.VerificationTokenByteLength));
            var verificationToken = new VerificationToken(
                Id: 0, // Will be set by database
                UserId: user.Id,
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
                new EmailTemplateData.EmailVerification(tokenString, _appOptions.BaseUrl),
                cancellationToken);

            logger.LogInformation("Resent verification email to {Email}", email);

            // Audit log
            await auditLog.LogEventAsync(
                AuditEventType.UserEmailVerificationSent,
                actor: Actor.FromSystem("email_verification"), // System event
                target: Actor.FromWebUser(user.Id),
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
            UserId: user.Id,
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
                new EmailTemplateData.PasswordReset(resetLink, ExpiryMinutes: 60),
                cancellationToken);

            logger.LogInformation("Password reset email sent to {Email}", email);

            // Audit log
            await auditLog.LogEventAsync(
                AuditEventType.UserPasswordResetRequested,
                actor: Actor.FromSystem("password_reset"), // System event
                target: Actor.FromWebUser(user.Id),
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
            ModifiedBy = user.Id,
            ModifiedAt = DateTimeOffset.UtcNow
        };

        await userRepository.UpdateAsync(updatedUser, cancellationToken);

        // Mark token as used
        await verificationTokenRepository.MarkAsUsedAsync(token, cancellationToken);

        logger.LogInformation("Password reset for {User}", LogDisplayName.WebUserInfo(user.Email, user.Id));

        // Audit log
        await auditLog.LogEventAsync(
            AuditEventType.UserPasswordChanged,
            actor: Actor.FromWebUser(user.Id),
            target: Actor.FromWebUser(user.Id),
            value: "Password reset via email",
            cancellationToken: cancellationToken);

        return true;
    }
}
