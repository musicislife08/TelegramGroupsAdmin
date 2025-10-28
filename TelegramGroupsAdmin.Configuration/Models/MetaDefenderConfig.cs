namespace TelegramGroupsAdmin.Configuration.Models;

/// <summary>
/// MetaDefender configuration
/// </summary>
public class MetaDefenderConfig
{
    /// <summary>
    /// Enable/disable MetaDefender scanning
    /// </summary>
    public bool Enabled { get; set; } = false;  // Requires API key

    /// <summary>
    /// Daily request limit
    /// </summary>
    public int DailyLimit { get; set; } = 40;
}
