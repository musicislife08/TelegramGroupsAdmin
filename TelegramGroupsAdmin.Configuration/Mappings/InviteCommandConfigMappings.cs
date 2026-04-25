using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Data.Models.Configs;

namespace TelegramGroupsAdmin.Configuration.Mappings;

public static class InviteCommandConfigMappings
{
    extension(InviteCommandConfigData data)
    {
        public InviteCommandConfig ToModel() => new()
        {
            Enabled = data.Enabled,
            DeleteCommandMessage = data.DeleteCommandMessage,
            DeleteResponseAfterSeconds = data.DeleteResponseAfterSeconds
        };
    }

    extension(InviteCommandConfig model)
    {
        public InviteCommandConfigData ToData() => new()
        {
            Enabled = model.Enabled,
            DeleteCommandMessage = model.DeleteCommandMessage,
            DeleteResponseAfterSeconds = model.DeleteResponseAfterSeconds
        };
    }
}
