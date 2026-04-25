using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Data.Models.Configs;

namespace TelegramGroupsAdmin.Configuration.Mappings;

public static class WarningSystemConfigMappings
{
    extension(WarningSystemConfigData data)
    {
        public WarningSystemConfig ToModel() => new()
        {
            AutoBanEnabled = data.AutoBanEnabled,
            AutoBanThreshold = data.AutoBanThreshold,
            AutoBanReason = data.AutoBanReason
        };
    }

    extension(WarningSystemConfig model)
    {
        public WarningSystemConfigData ToData() => new()
        {
            AutoBanEnabled = model.AutoBanEnabled,
            AutoBanThreshold = model.AutoBanThreshold,
            AutoBanReason = model.AutoBanReason
        };
    }
}
