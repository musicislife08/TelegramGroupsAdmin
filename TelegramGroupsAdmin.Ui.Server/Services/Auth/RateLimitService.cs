using System.Collections.Concurrent;
using System.Threading;
using TelegramGroupsAdmin.Ui.Server.Constants;

namespace TelegramGroupsAdmin.Ui.Server.Services.Auth;

/// <summary>
/// In-memory rate limiting service for authentication endpoints
/// Thread-safe implementation suitable for single-instance deployments
/// Uses per-key locking to avoid global contention across different users
/// </summary>
public class RateLimitService : IRateLimitService
{
    private readonly ILogger<RateLimitService> _logger;

    /// <summary>
    /// Per-key storage with embedded lock to avoid global contention.
    /// Each user/endpoint combination gets its own lock, preventing
    /// rate limit checks for user A from blocking checks for user B.
    /// </summary>
    private readonly ConcurrentDictionary<string, (Lock Lock, List<DateTimeOffset> Attempts)> _attempts = new();

    // Rate limit configurations (attempts / time window)
    private static readonly Dictionary<string, (int MaxAttempts, TimeSpan Window)> RateLimits = new()
    {
        ["login"] = (RateLimitConstants.LoginMaxAttempts, RateLimitConstants.LoginWindow),
        ["register"] = (RateLimitConstants.RegisterMaxAttempts, RateLimitConstants.RegisterWindow),
        ["totp_verify"] = (RateLimitConstants.TotpVerifyMaxAttempts, RateLimitConstants.TotpVerifyWindow),
        ["recovery_code"] = (RateLimitConstants.RecoveryCodeMaxAttempts, RateLimitConstants.RecoveryCodeWindow),
        ["resend_verification"] = (RateLimitConstants.ResendVerificationMaxAttempts, RateLimitConstants.ResendVerificationWindow),
        ["forgot_password"] = (RateLimitConstants.ForgotPasswordMaxAttempts, RateLimitConstants.ForgotPasswordWindow),
        ["reset_password"] = (RateLimitConstants.ResetPasswordMaxAttempts, RateLimitConstants.ResetPasswordWindow)
    };

    public RateLimitService(ILogger<RateLimitService> logger)
    {
        _logger = logger;
    }

    public Task<RateLimitCheckResult> CheckRateLimitAsync(string identifier, string endpointKey, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!RateLimits.TryGetValue(endpointKey, out var limit))
            {
                _logger.LogWarning("Unknown endpoint key for rate limiting: {EndpointKey}", endpointKey);
                return Task.FromResult(new RateLimitCheckResult(true, int.MaxValue, null));
            }

            var key = GetKey(identifier, endpointKey);
            var now = DateTimeOffset.UtcNow;

            // Get or create per-key storage with its own lock
            var (keyLock, attempts) = _attempts.GetOrAdd(key, _ => (new Lock(), new List<DateTimeOffset>()));

            // Thread-safe cleanup and check using per-key lock (not global)
            using (keyLock.EnterScope())
            {
                // Remove expired attempts
                attempts.RemoveAll(a => a < now - limit.Window);

                var recentAttempts = attempts.Count;
                var attemptsRemaining = Math.Max(0, limit.MaxAttempts - recentAttempts);

                if (recentAttempts >= limit.MaxAttempts)
                {
                    // Rate limit exceeded - calculate retry after
                    var oldestAttempt = attempts.Min();
                    var retryAfter = (oldestAttempt + limit.Window) - now;

                    _logger.LogWarning(
                        "Rate limit exceeded for {Identifier} on {Endpoint}: {Attempts}/{Max} attempts in {Window}",
                        identifier, endpointKey, recentAttempts, limit.MaxAttempts, limit.Window);

                    return Task.FromResult(new RateLimitCheckResult(false, 0, retryAfter));
                }

                return Task.FromResult(new RateLimitCheckResult(true, attemptsRemaining, null));
            }
        }
        catch (Exception ex)
        {
            // Fail open - don't block requests on rate limiter errors
            _logger.LogError(ex, "Error checking rate limit for {Identifier} on {Endpoint}", identifier, endpointKey);
            return Task.FromResult(new RateLimitCheckResult(true, int.MaxValue, null));
        }
    }

    public Task RecordAttemptAsync(string identifier, string endpointKey, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!RateLimits.ContainsKey(endpointKey))
            {
                _logger.LogWarning("Unknown endpoint key for rate limiting: {EndpointKey}", endpointKey);
                return Task.CompletedTask;
            }

            var key = GetKey(identifier, endpointKey);
            var now = DateTimeOffset.UtcNow;

            // Get or create per-key storage with its own lock
            var (keyLock, attempts) = _attempts.GetOrAdd(key, _ => (new Lock(), new List<DateTimeOffset>()));

            using (keyLock.EnterScope())
            {
                attempts.Add(now);
            }
        }
        catch (Exception ex)
        {
            // Fail open - don't crash on rate limiter errors
            _logger.LogError(ex, "Error recording rate limit attempt for {Identifier} on {Endpoint}", identifier, endpointKey);
        }

        return Task.CompletedTask;
    }

    private static string GetKey(string identifier, string endpointKey)
    {
        // Normalize email to lowercase for consistent keying
        return $"{endpointKey}:{identifier.ToLowerInvariant()}";
    }
}
