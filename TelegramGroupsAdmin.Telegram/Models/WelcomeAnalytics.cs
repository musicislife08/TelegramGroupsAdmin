namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Summary statistics for welcome system performance
/// </summary>
public class WelcomeStatsSummary
{
    /// <summary>Total number of users who joined in the date range</summary>
    public int TotalJoins { get; set; }

    /// <summary>Number of users who accepted the welcome message</summary>
    public int TotalAccepted { get; set; }

    /// <summary>Number of users who denied the welcome message</summary>
    public int TotalDenied { get; set; }

    /// <summary>Number of users who timed out</summary>
    public int TotalTimedOut { get; set; }

    /// <summary>Number of users who left before responding</summary>
    public int TotalLeft { get; set; }

    /// <summary>Percentage of users who accepted (out of total joins)</summary>
    public double AcceptanceRate { get; set; }

    /// <summary>Percentage of users who timed out (out of total joins)</summary>
    public double TimeoutRate { get; set; }

    /// <summary>Average time (in minutes) for users to accept after joining</summary>
    public double AverageMinutesToAccept { get; set; }
}

/// <summary>
/// Daily join trend for charting (date in user's timezone)
/// </summary>
public class DailyWelcomeJoinTrend
{
    /// <summary>Date in user's local timezone</summary>
    public DateOnly Date { get; set; }

    /// <summary>Number of users who joined on this date</summary>
    public int JoinCount { get; set; }
}

/// <summary>
/// Distribution of welcome response types with percentages
/// </summary>
public class WelcomeResponseDistribution
{
    /// <summary>Count of accepted responses</summary>
    public int AcceptedCount { get; set; }

    /// <summary>Count of denied responses</summary>
    public int DeniedCount { get; set; }

    /// <summary>Count of timeout responses</summary>
    public int TimeoutCount { get; set; }

    /// <summary>Count of left responses</summary>
    public int LeftCount { get; set; }

    /// <summary>Total responses (sum of all counts)</summary>
    public int TotalResponses { get; set; }

    /// <summary>Percentage who accepted</summary>
    public double AcceptedPercentage { get; set; }

    /// <summary>Percentage who denied</summary>
    public double DeniedPercentage { get; set; }

    /// <summary>Percentage who timed out</summary>
    public double TimeoutPercentage { get; set; }

    /// <summary>Percentage who left</summary>
    public double LeftPercentage { get; set; }
}

/// <summary>
/// Per-chat welcome statistics for breakdown table
/// </summary>
public class ChatWelcomeStats
{
    /// <summary>Chat ID</summary>
    public long ChatId { get; set; }

    /// <summary>Chat name from managed_chats</summary>
    public string ChatName { get; set; } = string.Empty;

    /// <summary>Total joins in this chat</summary>
    public int TotalJoins { get; set; }

    /// <summary>Number who accepted</summary>
    public int AcceptedCount { get; set; }

    /// <summary>Number who denied</summary>
    public int DeniedCount { get; set; }

    /// <summary>Number who timed out</summary>
    public int TimeoutCount { get; set; }

    /// <summary>Number who left</summary>
    public int LeftCount { get; set; }

    /// <summary>Acceptance rate percentage</summary>
    public double AcceptanceRate { get; set; }

    /// <summary>Timeout rate percentage</summary>
    public double TimeoutRate { get; set; }
}
