using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Data.Models.Configs;

namespace TelegramGroupsAdmin.Configuration.Mappings;

public static class BotProtectionConfigMappings
{
    extension(BotProtectionConfigData data)
    {
        public BotProtectionConfig ToModel() => new()
        {
            Enabled = data.Enabled,
            AutoBanBots = data.AutoBanBots,
            AllowAdminInvitedBots = data.AllowAdminInvitedBots,
            WhitelistedBots = data.WhitelistedBots.ToList(),
            LogBotEvents = data.LogBotEvents
        };
    }

    extension(BotProtectionConfig model)
    {
        public BotProtectionConfigData ToData() => new()
        {
            Enabled = model.Enabled,
            AutoBanBots = model.AutoBanBots,
            AllowAdminInvitedBots = model.AllowAdminInvitedBots,
            WhitelistedBots = model.WhitelistedBots.ToList(),
            LogBotEvents = model.LogBotEvents
        };
    }
}
