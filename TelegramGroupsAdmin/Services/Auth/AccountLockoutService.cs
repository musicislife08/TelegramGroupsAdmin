using TelegramGroupsAdmin.Constants;
using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Repositories;
using TelegramGroupsAdmin.Services.Email;

namespace TelegramGroupsAdmin.Services.Auth;

/// <summary>
/// Service for managing account lockout after failed login attempts (SECURITY-6)
/// Implements exponential backoff: 1min → 10min → 30min → 120min
/// </summary>
public class AccountLockoutService : IAccountLockoutService
{
    private readonly IUserRepository _userRepository;
    private readonly IEmailService _emailService;
    private readonly IAuditService _auditService;
    private readonly ILogger<AccountLockoutService> _logger;

    public AccountLockoutService(
        IUserRepository userRepository,
        IEmailService emailService,
        IAuditService auditService,
        ILogger<AccountLockoutService> logger)
    {
        _userRepository = userRepository;
        _emailService = emailService;
        _auditService = auditService;
        _logger = logger;
    }

    public async Task HandleFailedLoginAsync(WebUserIdentity user, CancellationToken cancellationToken = default)
    {
        // Increment failed login attempts counter
        await _userRepository.IncrementFailedLoginAttemptsAsync(user.Id, cancellationToken);

        // Get updated user to check threshold
        var dbUser = await _userRepository.GetByIdAsync(user.Id, cancellationToken);
        if (dbUser == null)
        {
            _logger.LogWarning("User {User} not found when handling failed login", user.ToLogDebug());
            return;
        }

        // Check if threshold reached
        if (dbUser.FailedLoginAttempts >= AccountLockoutConstants.MaxFailedAttempts)
        {
            // Calculate lockout duration using exponential backoff
            // Use number of previous lockouts to determine duration index
            var lockoutIndex = Math.Min(dbUser.FailedLoginAttempts - AccountLockoutConstants.MaxFailedAttempts, AccountLockoutConstants.LockoutDurations.Length - 1);
            var lockoutDuration = AccountLockoutConstants.LockoutDurations[lockoutIndex];
            var lockedUntil = DateTimeOffset.UtcNow + lockoutDuration;

            // Lock the account
            await _userRepository.LockAccountAsync(user.Id, lockedUntil, cancellationToken);

            _logger.LogWarning(
                "Account locked for {User} after {Attempts} failed attempts. Locked until {LockedUntil}",
                user.ToLogDebug(), dbUser.FailedLoginAttempts, lockedUntil);

            // Send email notification (fire-and-forget, don't block login flow)
            var email = user.Email ?? dbUser.WebUser.Email;
            if (email != null)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _emailService.SendTemplatedEmailAsync(
                            email,
                            EmailTemplate.AccountLocked,
                            new Dictionary<string, string>
                            {
                                ["email"] = email,
                                ["lockedUntil"] = lockedUntil.ToString("yyyy-MM-dd HH:mm:ss UTC"),
                                ["attempts"] = dbUser.FailedLoginAttempts.ToString()
                            },
                            cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to send account lockout email to {Email}", email);
                    }
                }, cancellationToken);
            }

            // Audit log the lockout event
            await _auditService.LogEventAsync(
                AuditEventType.UserAccountLocked,
                actor: Actor.FromSystem("account_lockout"),
                target: user.ToActor(),
                value: $"Account locked until {lockedUntil:yyyy-MM-dd HH:mm:ss UTC} after {dbUser.FailedLoginAttempts} failed login attempts",
                cancellationToken: cancellationToken);
        }
    }

    public async Task ResetLockoutAsync(WebUserIdentity user, CancellationToken cancellationToken = default)
    {
        await _userRepository.ResetFailedLoginAttemptsAsync(user.Id, cancellationToken);
        _logger.LogInformation("Reset lockout state for {User} after successful login",
            user.ToLogInfo());
    }

    public async Task UnlockAccountAsync(WebUserIdentity target, WebUserIdentity admin, CancellationToken cancellationToken = default)
    {
        // Get user before unlock
        var dbUser = await _userRepository.GetByIdAsync(target.Id, cancellationToken);
        if (dbUser == null)
        {
            _logger.LogWarning("User {User} not found when attempting manual unlock", target.ToLogDebug());
            return;
        }

        // Unlock the account
        await _userRepository.UnlockAccountAsync(target.Id, cancellationToken);

        _logger.LogInformation("Account manually unlocked for {Target} by {Admin}",
            target.ToLogInfo(),
            admin.ToLogInfo());

        // Send email notification (fire-and-forget)
        var email = target.Email ?? dbUser.WebUser.Email;
        if (email != null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _emailService.SendTemplatedEmailAsync(
                        email,
                        EmailTemplate.AccountUnlocked,
                        new Dictionary<string, string>
                        {
                            ["email"] = email
                        },
                        cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send account unlock email to {Email}", email);
                }
            }, cancellationToken);
        }

        // Audit log the unlock event
        await _auditService.LogEventAsync(
            AuditEventType.UserAccountUnlocked,
            actor: admin.ToActor(),
            target: target.ToActor(),
            value: "Account manually unlocked by administrator",
            cancellationToken: cancellationToken);
    }
}
