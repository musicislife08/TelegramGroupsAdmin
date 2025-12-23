namespace TelegramGroupsAdmin.Constants;

/// <summary>
/// Constants for account lockout after failed login attempts (SECURITY-6).
/// Implements exponential backoff: 1min → 10min → 30min → 120min.
/// </summary>
public static class AccountLockoutConstants
{
    /// <summary>
    /// Maximum failed login attempts before account lockout.
    /// </summary>
    public const int MaxFailedAttempts = 5;

    /// <summary>
    /// First lockout duration (1 minute).
    /// </summary>
    public static readonly TimeSpan FirstLockoutDuration = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Second lockout duration (10 minutes).
    /// </summary>
    public static readonly TimeSpan SecondLockoutDuration = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Third lockout duration (30 minutes).
    /// </summary>
    public static readonly TimeSpan ThirdLockoutDuration = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Fourth and subsequent lockout duration (120 minutes = 2 hours).
    /// </summary>
    public static readonly TimeSpan SubsequentLockoutDuration = TimeSpan.FromMinutes(120);

    /// <summary>
    /// All lockout durations in order.
    /// </summary>
    public static readonly TimeSpan[] LockoutDurations =
    [
        FirstLockoutDuration,
        SecondLockoutDuration,
        ThirdLockoutDuration,
        SubsequentLockoutDuration
    ];
}
