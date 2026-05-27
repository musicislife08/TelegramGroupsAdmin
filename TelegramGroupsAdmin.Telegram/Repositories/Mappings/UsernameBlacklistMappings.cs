using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories.Mappings;

internal static class UsernameBlacklistMappings
{
    extension(DataModels.UsernameBlacklistEntryDto data)
    {
        public UiModels.UsernameBlacklistEntry ToModel() => new(
            Id: data.Id,
            Pattern: data.Pattern,
            MatchType: (UiModels.BlacklistMatchType)data.MatchType,
            Enabled: data.Enabled,
            CreatedAt: data.CreatedAt,
            Notes: data.Notes);
    }

    extension(UiModels.UsernameBlacklistEntry ui)
    {
        public DataModels.UsernameBlacklistEntryDto ToDto() => new()
        {
            Id = ui.Id,
            Pattern = ui.Pattern,
            MatchType = (int)ui.MatchType,
            Enabled = ui.Enabled,
            CreatedAt = ui.CreatedAt,
            Notes = ui.Notes
        };
    }
}
