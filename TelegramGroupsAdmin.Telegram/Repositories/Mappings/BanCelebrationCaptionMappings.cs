using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories.Mappings;

/// <summary>
/// Mapping extensions for Ban Celebration Caption records
/// </summary>
public static class BanCelebrationCaptionMappings
{
    extension(DataModels.BanCelebrationCaptionDto data)
    {
        public UiModels.BanCelebrationCaption ToModel() => new()
        {
            Id = data.Id,
            Text = data.Text,
            DmText = data.DmText,
            Name = data.Name,
            CreatedAt = data.CreatedAt
        };
    }

    extension(UiModels.BanCelebrationCaption ui)
    {
        public DataModels.BanCelebrationCaptionDto ToDto() => new()
        {
            Id = ui.Id,
            Text = ui.Text,
            DmText = ui.DmText,
            Name = ui.Name,
            CreatedAt = ui.CreatedAt
        };
    }
}
