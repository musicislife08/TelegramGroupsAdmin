using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories.Mappings;

/// <summary>
/// Mapping extensions for ChatAdmin records
/// </summary>
public static class ChatAdminMappings
{
    extension(DataModels.ChatAdminRecordDto data)
    {
        public UiModels.ChatAdmin ToModel() => new()
        {
            Id = data.Id,
            ChatId = data.ChatId,
            TelegramId = data.TelegramId,
            Username = data.Username,
            IsCreator = data.IsCreator,
            PromotedAt = data.PromotedAt,
            LastVerifiedAt = data.LastVerifiedAt,
            IsActive = data.IsActive
        };
    }

    extension(UiModels.ChatAdmin ui)
    {
        public DataModels.ChatAdminRecordDto ToDto() => new()
        {
            Id = ui.Id,
            ChatId = ui.ChatId,
            TelegramId = ui.TelegramId,
            Username = ui.Username,
            IsCreator = ui.IsCreator,
            PromotedAt = ui.PromotedAt,
            LastVerifiedAt = ui.LastVerifiedAt,
            IsActive = ui.IsActive
        };
    }
}
