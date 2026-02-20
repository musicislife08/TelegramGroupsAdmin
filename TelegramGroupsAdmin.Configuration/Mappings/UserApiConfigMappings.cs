using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Data.Models.Configs;

namespace TelegramGroupsAdmin.Configuration.Mappings;

/// <summary>
/// Mapping extensions between Data layer DTO and Business layer model
/// for Telegram User API configuration.
/// </summary>
public static class UserApiConfigMappings
{
    extension(UserApiConfigData data)
    {
        public UserApiConfig ToModel() => new()
        {
            ApiId = data.ApiId
        };
    }

    extension(UserApiConfig model)
    {
        public UserApiConfigData ToData() => new()
        {
            ApiId = model.ApiId
        };
    }
}
