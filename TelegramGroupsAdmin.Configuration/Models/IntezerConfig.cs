namespace TelegramGroupsAdmin.Configuration.Models;

/// <summary>
/// Intezer configuration
/// </summary>
public class IntezerConfig
{
    /// <summary>
    /// Enable/disable Intezer scanning
    /// </summary>
    public bool Enabled { get; set; } = false;  // Requires API key

    /// <summary>
    /// Monthly request limit
    /// </summary>
    public int MonthlyLimit { get; set; } = 10;
}
