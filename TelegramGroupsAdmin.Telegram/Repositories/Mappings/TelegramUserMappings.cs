using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories.Mappings;

/// <summary>
/// Mapping extensions for TelegramUser records (User photo centralization + future features)
/// </summary>
public static class TelegramUserMappings
{
    extension(DataModels.TelegramUserDto data)
    {
        public UiModels.TelegramUser ToModel() => new(
            TelegramUserId: data.TelegramUserId,
            Username: data.Username,
            FirstName: data.FirstName,
            LastName: data.LastName,
            UserPhotoPath: data.UserPhotoPath,
            PhotoHash: data.PhotoHash,
            PhotoFileUniqueId: data.PhotoFileUniqueId,
            IsBot: data.IsBot,
            IsTrusted: data.IsTrusted,
            BotDmEnabled: data.BotDmEnabled,
            FirstSeenAt: data.FirstSeenAt,
            LastSeenAt: data.LastSeenAt,
            CreatedAt: data.CreatedAt,
            UpdatedAt: data.UpdatedAt,
            IsActive: data.IsActive
        );
    }

    extension(UiModels.TelegramUser ui)
    {
        public DataModels.TelegramUserDto ToDto() => new()
        {
            TelegramUserId = ui.TelegramUserId,
            Username = ui.Username,
            FirstName = ui.FirstName,
            LastName = ui.LastName,
            UserPhotoPath = ui.UserPhotoPath,
            PhotoHash = ui.PhotoHash,
            PhotoFileUniqueId = ui.PhotoFileUniqueId,
            IsBot = ui.IsBot,
            IsTrusted = ui.IsTrusted,
            IsActive = ui.IsActive,
            BotDmEnabled = ui.BotDmEnabled,
            FirstSeenAt = ui.FirstSeenAt,
            LastSeenAt = ui.LastSeenAt,
            CreatedAt = ui.CreatedAt,
            UpdatedAt = ui.UpdatedAt
        };
    }
}
