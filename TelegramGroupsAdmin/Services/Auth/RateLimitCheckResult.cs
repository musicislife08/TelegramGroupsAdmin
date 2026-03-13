namespace TelegramGroupsAdmin.Services.Auth;

/// <summary>
/// Result of a rate limit check
/// </summary>
/// <param name="IsAllowed">True if the request is allowed</param>
/// <param name="AttemptsRemaining">Number of attempts remaining before rate limit</param>
/// <param name="RetryAfter">Time span until rate limit resets (null if allowed)</param>
public record RateLimitCheckResult(bool IsAllowed, int AttemptsRemaining, TimeSpan? RetryAfter);
