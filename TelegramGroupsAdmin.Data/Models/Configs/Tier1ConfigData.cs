namespace TelegramGroupsAdmin.Data.Models.Configs;

/// <summary>
/// Data layer representation of Tier1Config for EF Core JSON column mapping.
/// </summary>
public class Tier1ConfigData
{
    /// <summary>
    /// ClamAV scanner settings
    /// </summary>
    public ClamAVConfigData ClamAV { get; set; } = new();
}
