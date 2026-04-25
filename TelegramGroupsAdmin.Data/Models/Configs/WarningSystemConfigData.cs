namespace TelegramGroupsAdmin.Data.Models.Configs;

/// <summary>
/// Data layer representation of WarningSystemConfig for EF Core JSON column mapping.
/// Maps to business model via ToModel/ToData extensions.
/// Multiplexed inside the moderation_config column via ModerationConfigData.
/// </summary>
public class WarningSystemConfigData
{
    public bool AutoBanEnabled { get; set; }
    public int AutoBanThreshold { get; set; }
    public string AutoBanReason { get; set; } = string.Empty;
}
