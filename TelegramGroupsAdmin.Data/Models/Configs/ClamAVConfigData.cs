namespace TelegramGroupsAdmin.Data.Models.Configs;

/// <summary>
/// Data layer representation of ClamAVConfig for EF Core JSON column mapping.
/// </summary>
public class ClamAVConfigData
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
    /// Scan timeout in seconds. Stored as double for consistency with other timeout configs.
    /// </summary>
    public double TimeoutSeconds { get; set; } = 30;
}
