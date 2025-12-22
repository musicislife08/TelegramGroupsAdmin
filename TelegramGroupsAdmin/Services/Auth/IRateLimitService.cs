namespace TelegramGroupsAdmin.Services.Auth;

/// <summary>
/// Service for rate limiting authentication attempts
/// </summary>
public interface IRateLimitService
{
    /// <summary>
    /// Checks if a rate limit has been exceeded for the given identifier and endpoint
    /// </summary>
    Task<RateLimitCheckResult> CheckRateLimitAsync(string identifier, string endpointKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records an attempt for rate limiting
    /// </summary>
    Task RecordAttemptAsync(string identifier, string endpointKey, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a rate limit check
/// </summary>
/// <param name="IsAllowed">True if the request is allowed</param>
/// <param name="AttemptsRemaining">Number of attempts remaining before rate limit</param>
/// <param name="RetryAfter">Time span until rate limit resets (null if allowed)</param>
public record RateLimitCheckResult(bool IsAllowed, int AttemptsRemaining, TimeSpan? RetryAfter);
