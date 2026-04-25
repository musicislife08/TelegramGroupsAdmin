namespace TelegramGroupsAdmin.Data.Models.Configs;

/// <summary>
/// Wrapper DTO multiplexing two configs inside the moderation_config JSONB column.
/// JSON shape: { "warningSystem": { ... }, "inviteCommand": { ... } }
/// Both children are nullable so partially-populated rows continue to deserialize.
/// </summary>
public class ModerationConfigData
{
    public WarningSystemConfigData? WarningSystem { get; set; }
    public InviteCommandConfigData? InviteCommand { get; set; }
}
