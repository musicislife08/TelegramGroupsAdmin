namespace TelegramGroupsAdmin.Configuration.Models;

/// <summary>
/// ClamAV configuration
/// </summary>
public class ClamAVConfig
{
    /// <summary>
    /// Enable/disable ClamAV scanning
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// ClamAV daemon host
    /// </summary>
    public string Host { get; set; } = "localhost";

    /// <summary>
    /// ClamAV daemon port
    /// </summary>
    public int Port { get; set; } = 3310;

    /// <summary>
    /// Scan timeout in seconds
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;
}
