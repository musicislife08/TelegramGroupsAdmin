namespace TelegramGroupsAdmin.Data.Models.Configs;

/// <summary>
/// Data layer representation of InviteCommandConfig for EF Core JSON column mapping.
/// Maps to business model via ToModel/ToData extensions.
/// Multiplexed inside the moderation_config column via ModerationConfigData.
/// </summary>
public class InviteCommandConfigData
{
    public bool Enabled { get; set; } = true;
    public bool DeleteCommandMessage { get; set; } = true;
    public int DeleteResponseAfterSeconds { get; set; } = 30;
}
