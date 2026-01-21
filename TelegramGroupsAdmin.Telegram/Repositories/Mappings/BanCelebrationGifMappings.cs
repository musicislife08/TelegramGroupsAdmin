using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories.Mappings;

/// <summary>
/// Mapping extensions for Ban Celebration GIF records
/// </summary>
public static class BanCelebrationGifMappings
{
    extension(DataModels.BanCelebrationGifDto data)
    {
        public UiModels.BanCelebrationGif ToModel() => new()
        {
            Id = data.Id,
            FilePath = data.FilePath,
            FileId = data.FileId,
            Name = data.Name,
            ThumbnailPath = data.ThumbnailPath,
            CreatedAt = data.CreatedAt
        };
    }

    extension(UiModels.BanCelebrationGif ui)
    {
        public DataModels.BanCelebrationGifDto ToDto() => new()
        {
            Id = ui.Id,
            FilePath = ui.FilePath,
            FileId = ui.FileId,
            Name = ui.Name,
            ThumbnailPath = ui.ThumbnailPath,
            CreatedAt = ui.CreatedAt
        };
    }
}
