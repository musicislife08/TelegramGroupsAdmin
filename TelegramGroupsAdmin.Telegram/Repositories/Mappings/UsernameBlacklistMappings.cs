using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Core.Repositories.Mappings;

namespace TelegramGroupsAdmin.Telegram.Repositories.Mappings;

internal static class UsernameBlacklistMappings
{
    extension(DataModels.UsernameBlacklistEntryDto data)
    {
        public UiModels.UsernameBlacklistEntry ToModel(
            string? webUserEmail = null,
            string? telegramUsername = null,
            string? telegramFirstName = null,
            string? telegramLastName = null)
        {
            return new UiModels.UsernameBlacklistEntry(
                Id: data.Id,
                Pattern: data.Pattern,
                MatchType: (DataModels.BlacklistMatchType)data.MatchType,
                Enabled: data.Enabled,
                CreatedAt: data.CreatedAt,
                CreatedBy: ActorMappings.ToActor(
                    data.WebUserId, data.TelegramUserId, data.SystemIdentifier,
                    webUserEmail, telegramUsername, telegramFirstName, telegramLastName),
                Notes: data.Notes);
        }
    }

    extension(UiModels.UsernameBlacklistEntry ui)
    {
        public DataModels.UsernameBlacklistEntryDto ToDto()
        {
            ActorMappings.SetActorColumns(ui.CreatedBy, out var webUserId, out var telegramUserId, out var systemIdentifier);

            return new()
            {
                Id = ui.Id,
                Pattern = ui.Pattern,
                MatchType = (int)ui.MatchType,
                Enabled = ui.Enabled,
                CreatedAt = ui.CreatedAt,
                WebUserId = webUserId,
                TelegramUserId = telegramUserId,
                SystemIdentifier = systemIdentifier,
                Notes = ui.Notes
            };
        }
    }
}
