using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Data.Models.Configs;

namespace TelegramGroupsAdmin.Configuration.Mappings;

public static class BanCelebrationConfigMappings
{
    extension(BanCelebrationConfigData data)
    {
        public BanCelebrationConfig ToModel() => new()
        {
            Enabled = data.Enabled,
            TriggerOnAutoBan = data.TriggerOnAutoBan,
            TriggerOnManualBan = data.TriggerOnManualBan,
            SendToBannedUser = data.SendToBannedUser
        };
    }

    extension(BanCelebrationConfig model)
    {
        public BanCelebrationConfigData ToData() => new()
        {
            Enabled = model.Enabled,
            TriggerOnAutoBan = model.TriggerOnAutoBan,
            TriggerOnManualBan = model.TriggerOnManualBan,
            SendToBannedUser = model.SendToBannedUser
        };
    }
}
