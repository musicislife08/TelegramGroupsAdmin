using System.Text.Json;
using TelegramGroupsAdmin.Core.Models;
using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories.Mappings;

/// <summary>
/// Mapping extensions for Managed Chat records
/// </summary>
public static class ManagedChatMappings
{
    public static UiModels.ManagedChatRecord ToModel(this DataModels.ManagedChatRecordDto data) => new(
        ChatId: data.ChatId,
        ChatName: data.ChatName,
        ChatType: (UiModels.ManagedChatType)data.ChatType,
        BotStatus: (UiModels.BotChatStatus)data.BotStatus,
        IsAdmin: data.IsAdmin,
        AddedAt: data.AddedAt,
        IsActive: data.IsActive,
        LastSeenAt: data.LastSeenAt,
        SettingsJson: data.SettingsJson,
        ChatIconPath: data.ChatIconPath
    );

    public static DataModels.ManagedChatRecordDto ToDto(this UiModels.ManagedChatRecord ui) => new()
    {
        ChatId = ui.ChatId,
        ChatName = ui.ChatName,
        ChatType = (DataModels.ManagedChatType)(int)ui.ChatType,
        BotStatus = (DataModels.BotChatStatus)(int)ui.BotStatus,
        IsAdmin = ui.IsAdmin,
        AddedAt = ui.AddedAt,
        IsActive = ui.IsActive,
        LastSeenAt = ui.LastSeenAt,
        SettingsJson = ui.SettingsJson,
        ChatIconPath = ui.ChatIconPath
    };
}
