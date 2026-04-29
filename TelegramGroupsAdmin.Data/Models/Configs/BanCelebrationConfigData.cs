namespace TelegramGroupsAdmin.Data.Models.Configs;

/// <summary>
/// Data layer representation of BanCelebrationConfig for EF Core JSON column mapping.
/// Maps to business model via ToModel/ToData extensions.
/// </summary>
public class BanCelebrationConfigData
{
    public bool Enabled { get; set; }
    public bool TriggerOnAutoBan { get; set; } = true;
    public bool TriggerOnManualBan { get; set; } = true;
    public bool SendToBannedUser { get; set; } = true;
}
