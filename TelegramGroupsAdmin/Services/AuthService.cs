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
    VerificationTokenRepository verificationTokenRepository,
    IAuditService auditLog,
    ITotpService totpService,
    IPasswordHasher passwordHasher,
    IEmailService emailService,
    IAccountLockoutService accountLockoutService,
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

        // If TOTP is enabled, require verification
        if (user.TotpEnabled)
        {
            return new AuthResult(true, user.Id, user.Email, user.PermissionLevelInt, true, true, null);
        }

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

        // Determine permission level and validate invite
        string? invitedBy = null;
        PermissionLevel permissionLevel;

        if (isFirstRun)
        {
            permissionLevel = PermissionLevel.Owner;
            logger.LogInformation("First run detected - creating owner account");
        }
        else
        {
            // Validate invite token for subsequent users
            var inviteValidation = await ValidateInviteTokenAsync(inviteToken, ct);
            if (!inviteValidation.IsValid)
            {
                return new RegisterResult(false, null, inviteValidation.ErrorMessage);
            }

            invitedBy = inviteValidation.InvitedBy;
            permissionLevel = inviteValidation.PermissionLevel;
        }

        // Check if email already exists (including deleted users)
        var existing = await userRepository.GetByEmailIncludingDeletedAsync(email, ct);
        if (existing != null)
        {
            if (existing.Status != UserStatus.Deleted)
            {
                logger.LogWarning("Registration attempt for existing active/disabled user: {Email}", email);
                return new RegisterResult(false, null, "Email already registered");
            }

            // Reactivate deleted user
            return await ReactivateUserAsync(existing, password, permissionLevel, invitedBy, isFirstRun, inviteToken, email, ct);
        }

        // Create new user
        return await CreateNewUserAsync(email, password, permissionLevel, invitedBy, isFirstRun, inviteToken, ct);
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

    public async Task<bool> AdminClearTotpSetupAsync(string targetUserId, string adminUserId, CancellationToken ct = default)
    {
        return await totpService.AdminClearTotpSetupAsync(targetUserId, adminUserId, ct);
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

    private async Task<RegisterResult> ReactivateUserAsync(
        UserRecord existing,
        string password,
        PermissionLevel permissionLevel,
        string? invitedBy,
        bool isFirstRun,
        string? inviteToken,
        string email,
        CancellationToken ct)
    {
        // Reactivate deleted user with fresh credentials
        var reactivatedUser = existing with
        {
            PasswordHash = passwordHasher.HashPassword(password),
            SecurityStamp = Guid.NewGuid().ToString(),
            PermissionLevel = permissionLevel,
            InvitedBy = invitedBy,
            Status = UserStatus.Active,
            IsActive = true,
            TotpSecret = null,
            TotpEnabled = false,
            ModifiedBy = existing.Id,
            ModifiedAt = DateTimeOffset.UtcNow,
            EmailVerified = isFirstRun,
            EmailVerificationToken = null,
            EmailVerificationTokenExpiresAt = null
        };

        await userRepository.UpdateAsync(reactivatedUser, ct);
        logger.LogInformation("Reactivated deleted user: {UserId}", existing.Id);

        // Mark invite as used (if not first run)
        if (!isFirstRun && !string.IsNullOrEmpty(inviteToken))
        {
            await userRepository.UseInviteAsync(inviteToken, existing.Id, ct);

            // Audit log - user reactivation via invite
            await auditLog.LogEventAsync(
                AuditEventType.UserRegistered,
                actor: Actor.FromWebUser(existing.Id),
                target: Actor.FromWebUser(existing.Id),
                value: $"Reactivated via invite from {invitedBy}",
                ct: ct);

            // Send verification email
            await SendVerificationEmailAsync(existing.Id, email, ct);
        }
        else
        {
            // Audit log - user reactivation (first run)
            await auditLog.LogEventAsync(
                AuditEventType.UserRegistered,
                actor: Actor.FromWebUser(existing.Id),
                target: Actor.FromWebUser(existing.Id),
                value: "Reactivated (first run)",
                ct: ct);
        }

        return new RegisterResult(true, existing.Id, null);
    }

    private async Task<RegisterResult> CreateNewUserAsync(
        string email,
        string password,
        PermissionLevel permissionLevel,
        string? invitedBy,
        bool isFirstRun,
        string? inviteToken,
        CancellationToken ct)
    {
        // Create user
        var userId = Guid.NewGuid().ToString();
        var passwordHash = passwordHasher.HashPassword(password);
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
            TotpSetupStartedAt: null,
            CreatedAt: DateTimeOffset.UtcNow,
            LastLoginAt: null,
            Status: UserStatus.Active,
            ModifiedBy: null,
            ModifiedAt: null,
            EmailVerified: isFirstRun,
            EmailVerificationToken: null,
            EmailVerificationTokenExpiresAt: null,
            PasswordResetToken: null,
            PasswordResetTokenExpiresAt: null,
            FailedLoginAttempts: 0,
            LockedUntil: null
        );

        await userRepository.CreateAsync(user, ct);

        // Mark invite as used (if not first run)
        if (!isFirstRun && !string.IsNullOrEmpty(inviteToken))
        {
            await userRepository.UseInviteAsync(inviteToken, userId, ct);
            logger.LogInformation("New user registered: {UserId} via invite from {InviterId}", userId, invitedBy);

            // Audit log - user registration via invite
            await auditLog.LogEventAsync(
                AuditEventType.UserRegistered,
                actor: Actor.FromWebUser(userId),
                target: Actor.FromWebUser(userId),
                value: $"Registered via invite from {invitedBy}",
                ct: ct);

            // Send verification email
            await SendVerificationEmailAsync(userId, email, ct);
        }
        else
        {
            logger.LogInformation("Owner account created: {UserId} (first run)", userId);

            // Audit log - owner account creation (first run)
            await auditLog.LogEventAsync(
                AuditEventType.UserRegistered,
                actor: Actor.FromWebUser(userId),
                target: Actor.FromWebUser(userId),
                value: "First user (Owner)",
                ct: ct);
        }

        return new RegisterResult(true, userId, null);
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
