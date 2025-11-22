namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Breakdown of messages by user trust level (UX-2.1)
/// Uses spam_check_skip_reason to determine trust status at message time
/// </summary>
public record TrustedUserBreakdown
{
    /// <summary>
    /// Number of messages from trusted users (spam_check_skip_reason = UserTrusted)
    /// </summary>
    public int TrustedMessages { get; init; }

    /// <summary>
    /// Number of messages from untrusted users (spam_check_skip_reason = NotSkipped)
    /// </summary>
    public int UntrustedMessages { get; init; }

    /// <summary>
    /// Number of messages from admin users (spam_check_skip_reason = UserAdmin)
    /// </summary>
    public int AdminMessages { get; init; }

    /// <summary>
    /// Percentage of messages from trusted users
    /// </summary>
    public double TrustedPercentage { get; init; }

    /// <summary>
    /// Percentage of messages from untrusted users
    /// </summary>
    public double UntrustedPercentage { get; init; }

    /// <summary>
    /// Percentage of messages from admin users
    /// </summary>
    public double AdminPercentage { get; init; }
}
