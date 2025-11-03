using System.Collections.Concurrent;

namespace TelegramGroupsAdmin.Services.Auth;

/// <summary>
/// In-memory rate limiting service for authentication endpoints
/// Thread-safe implementation suitable for single-instance deployments
/// </summary>
public class RateLimitService : IRateLimitService
{
    private readonly ILogger<RateLimitService> _logger;
    private readonly ConcurrentDictionary<string, List<DateTimeOffset>> _attempts = new();

    // Rate limit configurations (attempts / time window)
    private static readonly Dictionary<string, (int MaxAttempts, TimeSpan Window)> RateLimits = new()
    {
        ["login"] = (5, TimeSpan.FromMinutes(15)),
        ["register"] = (3, TimeSpan.FromHours(1)),
        ["totp_verify"] = (5, TimeSpan.FromMinutes(5)),
        ["recovery_code"] = (5, TimeSpan.FromMinutes(5)),
        ["resend_verification"] = (5, TimeSpan.FromHours(1)),
        ["forgot_password"] = (3, TimeSpan.FromHours(1)),
        ["reset_password"] = (3, TimeSpan.FromHours(1))
    };

    public RateLimitService(ILogger<RateLimitService> logger)
    {
        _logger = logger;
    }

    public Task<RateLimitCheckResult> CheckRateLimitAsync(string identifier, string endpointKey, CancellationToken ct = default)
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

            // Get or create attempt list
            var attempts = _attempts.GetOrAdd(key, _ => new List<DateTimeOffset>());

            // Thread-safe cleanup and check
            lock (attempts)
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

    public Task RecordAttemptAsync(string identifier, string endpointKey, CancellationToken ct = default)
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

            var attempts = _attempts.GetOrAdd(key, _ => new List<DateTimeOffset>());

            lock (attempts)
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
