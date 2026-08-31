using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Data.Models.Configs;

namespace TelegramGroupsAdmin.Configuration.Mappings;

public static class TelegramBotConfigMappings
{
    extension(TelegramBotConfigData data)
    {
        public TelegramBotConfig ToModel() => new()
        {
            BotEnabled = data.BotEnabled
        };
    }

    extension(TelegramBotConfig model)
    {
        public TelegramBotConfigData ToData() => new()
        {
            BotEnabled = model.BotEnabled
        };
    }
}
