using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories.Mappings;

/// <summary>
/// Mapping extensions for Managed Chat records
/// </summary>
public static class ManagedChatMappings
{
    extension(DataModels.ManagedChatRecordDto data)
    {
        public UiModels.ManagedChatRecord ToModel() => new(
            ChatId: data.ChatId,
            ChatName: data.ChatName,
            ChatType: (UiModels.ManagedChatType)data.ChatType,
            BotStatus: (UiModels.BotChatStatus)data.BotStatus,
            IsAdmin: data.IsAdmin,
            AddedAt: data.AddedAt,
            IsActive: data.IsActive,
            IsDeleted: data.IsDeleted,
            LastSeenAt: data.LastSeenAt,
            SettingsJson: data.SettingsJson,
            ChatIconPath: data.ChatIconPath
        );
    }

    extension(UiModels.ManagedChatRecord ui)
    {
        public DataModels.ManagedChatRecordDto ToDto() => new()
        {
            ChatId = ui.ChatId,
            ChatName = ui.ChatName,
            ChatType = (DataModels.ManagedChatType)(int)ui.ChatType,
            BotStatus = (DataModels.BotChatStatus)(int)ui.BotStatus,
            IsAdmin = ui.IsAdmin,
            AddedAt = ui.AddedAt,
            IsActive = ui.IsActive,
            IsDeleted = ui.IsDeleted,
            LastSeenAt = ui.LastSeenAt,
            SettingsJson = ui.SettingsJson,
            ChatIconPath = ui.ChatIconPath
        };
    }
}
