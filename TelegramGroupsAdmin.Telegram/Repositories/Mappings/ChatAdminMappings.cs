using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories.Mappings;

/// <summary>
/// Mapping extensions for ChatAdmin records
/// </summary>
public static class ChatAdminMappings
{
    public static UiModels.ChatAdmin ToModel(this DataModels.ChatAdminRecordDto data) => new()
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

    public static DataModels.ChatAdminRecordDto ToDto(this UiModels.ChatAdmin ui) => new()
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
