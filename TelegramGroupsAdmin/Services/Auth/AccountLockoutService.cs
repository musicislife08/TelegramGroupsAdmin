using TelegramGroupsAdmin.Constants;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Repositories;
using TelegramGroupsAdmin.Services.Email;
using TelegramGroupsAdmin.Telegram.Models;

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

    public async Task HandleFailedLoginAsync(string userId, CancellationToken cancellationToken = default)
    {
        // Increment failed login attempts counter
        await _userRepository.IncrementFailedLoginAttemptsAsync(userId, cancellationToken);

        // Get updated user to check threshold
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user == null)
        {
            _logger.LogWarning("User {UserId} not found when handling failed login", userId);
            return;
        }

        // Check if threshold reached
        if (user.FailedLoginAttempts >= AccountLockoutConstants.MaxFailedAttempts)
        {
            // Calculate lockout duration using exponential backoff
            // Use number of previous lockouts to determine duration index
            var lockoutIndex = Math.Min(user.FailedLoginAttempts - AccountLockoutConstants.MaxFailedAttempts, AccountLockoutConstants.LockoutDurations.Length - 1);
            var lockoutDuration = AccountLockoutConstants.LockoutDurations[lockoutIndex];
            var lockedUntil = DateTimeOffset.UtcNow + lockoutDuration;

            // Lock the account
            await _userRepository.LockAccountAsync(userId, lockedUntil, cancellationToken);

            _logger.LogWarning(
                "Account locked for {User} after {Attempts} failed attempts. Locked until {LockedUntil}",
                LogDisplayName.WebUserDebug(user.Email, userId), user.FailedLoginAttempts, lockedUntil);

            // Send email notification (fire-and-forget, don't block login flow)
            _ = Task.Run(async () =>
            {
                try
                {
                    await _emailService.SendTemplatedEmailAsync(
                        user.Email,
                        EmailTemplate.AccountLocked,
                        new Dictionary<string, string>
                        {
                            ["email"] = user.Email,
                            ["lockedUntil"] = lockedUntil.ToString("yyyy-MM-dd HH:mm:ss UTC"),
                            ["attempts"] = user.FailedLoginAttempts.ToString()
                        },
                        cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send account lockout email to {Email}", user.Email);
                }
            }, cancellationToken);

            // Audit log the lockout event
            await _auditService.LogEventAsync(
                AuditEventType.UserAccountLocked,
                actor: Actor.FromSystem("account_lockout"),
                target: Actor.FromWebUser(userId),
                value: $"Account locked until {lockedUntil:yyyy-MM-dd HH:mm:ss UTC} after {user.FailedLoginAttempts} failed login attempts",
                cancellationToken: cancellationToken);
        }
    }

    public async Task ResetLockoutAsync(string userId, CancellationToken cancellationToken = default)
    {
        await _userRepository.ResetFailedLoginAttemptsAsync(userId, cancellationToken);
        _logger.LogInformation("Reset lockout state for user {UserId} after successful login", userId);
    }

    public async Task UnlockAccountAsync(string userId, string unlockedBy, CancellationToken cancellationToken = default)
    {
        // Get user before unlock
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user == null)
        {
            _logger.LogWarning("User {UserId} not found when attempting manual unlock", userId);
            return;
        }

        // Unlock the account
        await _userRepository.UnlockAccountAsync(userId, cancellationToken);

        _logger.LogInformation("Account manually unlocked for {User} by {UnlockedBy}",
            LogDisplayName.WebUserInfo(user.Email, userId), unlockedBy);

        // Send email notification (fire-and-forget)
        _ = Task.Run(async () =>
        {
            try
            {
                await _emailService.SendTemplatedEmailAsync(
                    user.Email,
                    EmailTemplate.AccountUnlocked,
                    new Dictionary<string, string>
                    {
                        ["email"] = user.Email
                    },
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send account unlock email to {Email}", user.Email);
            }
        }, cancellationToken);

        // Audit log the unlock event
        await _auditService.LogEventAsync(
            AuditEventType.UserAccountUnlocked,
            actor: Actor.FromWebUser(unlockedBy),
            target: Actor.FromWebUser(userId),
            value: "Account manually unlocked by administrator",
            cancellationToken: cancellationToken);
    }
}
