namespace TelegramGroupsAdmin.Models.Analytics;

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
