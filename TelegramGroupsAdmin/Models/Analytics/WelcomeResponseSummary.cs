namespace TelegramGroupsAdmin.Models.Analytics;

/// <summary>
/// Pre-aggregated welcome response statistics from the welcome_response_summary view.
/// Grouped by chat and date for efficient analytics queries.
/// </summary>
public class WelcomeResponseSummary
{
    /// <summary>
    /// Chat ID for the welcome responses
    /// </summary>
    public long ChatId { get; set; }

    /// <summary>
    /// Chat name from managed_chats
    /// </summary>
    public string? ChatName { get; set; }

    /// <summary>
    /// Date of the joins (UTC)
    /// </summary>
    public DateOnly JoinDate { get; set; }

    /// <summary>
    /// Total number of joins for this chat on this date
    /// </summary>
    public int TotalJoins { get; set; }

    /// <summary>
    /// Number of accepted (passed challenge) responses
    /// </summary>
    public int AcceptedCount { get; set; }

    /// <summary>
    /// Number of denied (failed challenge) responses
    /// </summary>
    public int DeniedCount { get; set; }

    /// <summary>
    /// Number of timeout responses
    /// </summary>
    public int TimeoutCount { get; set; }

    /// <summary>
    /// Number of users who left before responding
    /// </summary>
    public int LeftCount { get; set; }

    /// <summary>
    /// Average time in seconds for accepted responses (nullable if no accepts)
    /// </summary>
    public double? AvgAcceptSeconds { get; set; }

    /// <summary>
    /// Acceptance rate as percentage (0-100)
    /// </summary>
    public double AcceptanceRate => TotalJoins > 0
        ? (AcceptedCount / (double)TotalJoins * 100.0)
        : 0;
}
