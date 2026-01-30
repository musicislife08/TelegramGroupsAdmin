namespace TelegramGroupsAdmin.Data.Models.Configs;

/// <summary>
/// Data layer representation of ImpersonationConfig for EF Core JSON column mapping.
/// </summary>
public class ImpersonationConfigData
{
    /// <summary>
    /// Whether impersonation detection is enabled on user join.
    /// </summary>
    public bool Enabled { get; set; } = true;
}
