using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories.Mappings;

/// <summary>
/// Mapping extensions for TelegramLinkToken records
/// </summary>
public static class TelegramLinkTokenMappings
{
    extension(DataModels.TelegramLinkTokenRecordDto data)
    {
        public UiModels.TelegramLinkTokenRecord ToModel() => new(
            Token: data.Token,
            UserId: data.UserId,
            CreatedAt: data.CreatedAt,
            ExpiresAt: data.ExpiresAt,
            UsedAt: data.UsedAt,
            UsedByTelegramId: data.UsedByTelegramId
        );
    }

    extension(UiModels.TelegramLinkTokenRecord ui)
    {
        public DataModels.TelegramLinkTokenRecordDto ToDto() => new()
        {
            Token = ui.Token,
            UserId = ui.UserId,
            CreatedAt = ui.CreatedAt,
            ExpiresAt = ui.ExpiresAt,
            UsedAt = ui.UsedAt,
            UsedByTelegramId = ui.UsedByTelegramId
        };
    }
}
