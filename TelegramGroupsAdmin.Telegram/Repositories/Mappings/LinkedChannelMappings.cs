using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories.Mappings;

/// <summary>
/// Mapping extensions for LinkedChannel records
/// </summary>
public static class LinkedChannelMappings
{
    extension(DataModels.LinkedChannelRecordDto data)
    {
        public UiModels.LinkedChannelRecord ToModel() => new(
            Id: data.Id,
            ManagedChatId: data.ManagedChatId,
            ChannelId: data.ChannelId,
            ChannelName: data.ChannelName,
            ChannelIconPath: data.ChannelIconPath,
            PhotoHash: data.PhotoHash,
            LastSynced: data.LastSynced
        );
    }

    extension(UiModels.LinkedChannelRecord ui)
    {
        public DataModels.LinkedChannelRecordDto ToDto() => new()
        {
            Id = ui.Id,
            ManagedChatId = ui.ManagedChatId,
            ChannelId = ui.ChannelId,
            ChannelName = ui.ChannelName,
            ChannelIconPath = ui.ChannelIconPath,
            PhotoHash = ui.PhotoHash,
            LastSynced = ui.LastSynced
        };
    }
}
