using TelegramGroupsAdmin.Data.Models.Configs;

namespace TelegramGroupsAdmin.Configuration.Mappings;

/// <summary>
/// Mapping helpers for the ModerationConfigData wrapper that multiplexes
/// WarningSystemConfig and InviteCommandConfig inside the moderation_config column.
/// Used by ConfigRepository.SaveWarningSystemAsync / SaveInviteCommandAsync to
/// merge updates without clobbering the sibling config in the same JSON blob.
/// </summary>
public static class ModerationConfigMappings
{
    extension(ModerationConfigData data)
    {
        /// <summary>
        /// Returns a copy of the wrapper with WarningSystem replaced.
        /// </summary>
        public ModerationConfigData WithWarningSystem(WarningSystemConfigData? warningSystem) => new()
        {
            WarningSystem = warningSystem,
            InviteCommand = data.InviteCommand
        };

        /// <summary>
        /// Returns a copy of the wrapper with InviteCommand replaced.
        /// </summary>
        public ModerationConfigData WithInviteCommand(InviteCommandConfigData? inviteCommand) => new()
        {
            WarningSystem = data.WarningSystem,
            InviteCommand = inviteCommand
        };
    }
}
