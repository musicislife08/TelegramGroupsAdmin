namespace TelegramGroupsAdmin.Models.Analytics;

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
