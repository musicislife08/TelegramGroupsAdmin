using System.Text.Json;
using TelegramGroupsAdmin.Core.Models;
using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories.Mappings;

/// <summary>
/// Mapping extensions for Message Edit records
/// </summary>
public static class MessageEditMappings
{
    public static UiModels.MessageEditRecord ToModel(this DataModels.MessageEditRecordDto data) => new(
        Id: data.Id,
        MessageId: data.MessageId,
        OldText: data.OldText,
        NewText: data.NewText,
        EditDate: data.EditDate,
        OldContentHash: data.OldContentHash,
        NewContentHash: data.NewContentHash
    );

    public static DataModels.MessageEditRecordDto ToDto(this UiModels.MessageEditRecord ui) => new()
    {
        Id = ui.Id,
        MessageId = ui.MessageId,
        EditDate = ui.EditDate,
        OldText = ui.OldText,
        NewText = ui.NewText,
        OldContentHash = ui.OldContentHash,
        NewContentHash = ui.NewContentHash
    };
}
