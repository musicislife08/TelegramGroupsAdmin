using System.Text.Json;
using TelegramGroupsAdmin.Core.Models;
using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories.Mappings;

/// <summary>
/// Mapping extensions for TelegramLinkToken records
/// </summary>
public static class TelegramLinkTokenMappings
{
    public static UiModels.TelegramLinkTokenRecord ToModel(this DataModels.TelegramLinkTokenRecordDto data) => new(
        Token: data.Token,
        UserId: data.UserId,
        CreatedAt: data.CreatedAt,
        ExpiresAt: data.ExpiresAt,
        UsedAt: data.UsedAt,
        UsedByTelegramId: data.UsedByTelegramId
    );

    public static DataModels.TelegramLinkTokenRecordDto ToDto(this UiModels.TelegramLinkTokenRecord ui) => new()
    {
        Token = ui.Token,
        UserId = ui.UserId,
        CreatedAt = ui.CreatedAt,
        ExpiresAt = ui.ExpiresAt,
        UsedAt = ui.UsedAt,
        UsedByTelegramId = ui.UsedByTelegramId
    };
}
