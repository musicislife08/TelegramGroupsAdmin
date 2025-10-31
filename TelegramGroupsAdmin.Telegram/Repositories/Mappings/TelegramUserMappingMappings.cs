using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories.Mappings;

/// <summary>
/// Mapping extensions for TelegramUserMapping records
/// </summary>
public static class TelegramUserMappingMappings
{
    public static UiModels.TelegramUserMappingRecord ToModel(this DataModels.TelegramUserMappingRecordDto data) => new(
        Id: data.Id,
        TelegramId: data.TelegramId,
        TelegramUsername: data.TelegramUsername,
        UserId: data.UserId,
        LinkedAt: data.LinkedAt,
        IsActive: data.IsActive
    );

    public static DataModels.TelegramUserMappingRecordDto ToDto(this UiModels.TelegramUserMappingRecord ui) => new()
    {
        Id = ui.Id,
        TelegramId = ui.TelegramId,
        TelegramUsername = ui.TelegramUsername,
        UserId = ui.UserId,
        LinkedAt = ui.LinkedAt,
        IsActive = ui.IsActive
    };
}
