using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Data.Models.Configs;

namespace TelegramGroupsAdmin.Configuration.Mappings;

public static class LogConfigMappings
{
    extension(LogConfigData data)
    {
        public LogConfig ToModel() => new()
        {
            DefaultLevel = (LogLevel)data.DefaultLevel,
            Overrides = data.Overrides.ToDictionary(kv => kv.Key, kv => (LogLevel)kv.Value),
            LastModified = data.LastModified
        };
    }

    extension(LogConfig model)
    {
        public LogConfigData ToData() => new()
        {
            DefaultLevel = (int)model.DefaultLevel,
            Overrides = model.Overrides.ToDictionary(kv => kv.Key, kv => (int)kv.Value),
            LastModified = model.LastModified
        };
    }
}
