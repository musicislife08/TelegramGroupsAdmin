namespace TelegramGroupsAdmin.Configuration.Models;

/// <summary>
/// Hybrid Analysis configuration
/// </summary>
public class HybridAnalysisConfig
{
    /// <summary>
    /// Enable/disable Hybrid Analysis scanning
    /// </summary>
    public bool Enabled { get; set; } = false;  // Requires API key

    /// <summary>
    /// Monthly request limit
    /// </summary>
    public int MonthlyLimit { get; set; } = 30;
}
