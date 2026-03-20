using System.Text.Json;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Models;
using TelegramGroupsAdmin.Repositories;
using TelegramGroupsAdmin.Services.Auth;

namespace TelegramGroupsAdmin.Services;

internal static class BootstrapOwnerService
{
    public static async Task<BootstrapResult> ExecuteAsync(
        string? filePath,
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        IAuditService auditService,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        // 1. DB-first idempotency: stop at first row if any user exists
        if (await userRepository.AnyUsersExistAsync(cancellationToken: cancellationToken))
        {
            logger.LogInformation("Bootstrap skipped: users already exist in the database");
            return new BootstrapResult(true, "already bootstrapped");
        }

        // 2. Validate file path argument
        if (filePath is null)
        {
            return new BootstrapResult(false, "--bootstrap requires a file path argument");
        }

        // 3. Check file exists
        if (!File.Exists(filePath))
        {
            return new BootstrapResult(false, $"Bootstrap file not found: {filePath}");
        }

        // 4. Read and validate file content
        var json = await File.ReadAllTextAsync(filePath, cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new BootstrapResult(false, "Bootstrap file is empty");
        }

        // 5. Parse JSON (invalid JSON is a hard failure)
        BootstrapCredentials? credentials;
        try
        {
            credentials = JsonSerializer.Deserialize<BootstrapCredentials>(json);
        }
        catch (JsonException)
        {
            return new BootstrapResult(false, "Bootstrap file contains invalid JSON");
        }

        // 6. Validate credentials content
        if (credentials is null ||
            string.IsNullOrEmpty(credentials.Email) ||
            !credentials.Email.Contains('@') ||
            string.IsNullOrEmpty(credentials.Password))
        {
            return new BootstrapResult(false,
                "Bootstrap file must contain non-empty 'email' (with @) and 'password' fields");
        }

        // 7. Create the Owner user
        var userId = Guid.NewGuid().ToString();
        var user = new UserRecord(
            WebUser: new WebUserIdentity(userId, credentials.Email, PermissionLevel.Owner),
            NormalizedEmail: credentials.Email.ToUpperInvariant(),
            PasswordHash: passwordHasher.HashPassword(credentials.Password),
            SecurityStamp: Guid.NewGuid().ToString(),
            InvitedBy: null,
            IsActive: true,
            TotpSecret: null,
            TotpEnabled: true,
            TotpSetupStartedAt: null,
            CreatedAt: DateTimeOffset.UtcNow,
            LastLoginAt: null,
            Status: UserStatus.Active,
            ModifiedBy: null,
            ModifiedAt: null,
            EmailVerified: true,
            EmailVerificationToken: null,
            EmailVerificationTokenExpiresAt: null,
            PasswordResetToken: null,
            PasswordResetTokenExpiresAt: null,
            FailedLoginAttempts: 0,
            LockedUntil: null
        );
        await userRepository.CreateAsync(user, cancellationToken: cancellationToken);
        logger.LogInformation("Owner account created via bootstrap: {Email}", credentials.Email);

        // 8. Audit log (non-fatal)
        try
        {
            await auditService.LogEventAsync(
                AuditEventType.UserRegistered,
                actor: Actor.Bootstrap,
                target: Actor.FromWebUser(userId),
                value: "Owner account created via --bootstrap CLI flag",
                cancellationToken: cancellationToken);
        }
        catch (Exception auditEx)
        {
            logger.LogWarning(auditEx, "Audit log entry failed after bootstrap (non-fatal)");
        }

        return new BootstrapResult(true, "Bootstrap complete");
    }
}
