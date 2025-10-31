using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories.Mappings;

/// <summary>
/// Mapping extensions for TelegramUser records (User photo centralization + future features)
/// </summary>
public static class TelegramUserMappings
{
    public static UiModels.TelegramUser ToModel(this DataModels.TelegramUserDto data) => new(
        TelegramUserId: data.TelegramUserId,
        Username: data.Username,
        FirstName: data.FirstName,
        LastName: data.LastName,
        UserPhotoPath: data.UserPhotoPath,
        PhotoHash: data.PhotoHash,
        PhotoFileUniqueId: data.PhotoFileUniqueId,
        IsTrusted: data.IsTrusted,
        BotDmEnabled: data.BotDmEnabled,
        FirstSeenAt: data.FirstSeenAt,
        LastSeenAt: data.LastSeenAt,
        CreatedAt: data.CreatedAt,
        UpdatedAt: data.UpdatedAt
    );

    public static DataModels.TelegramUserDto ToDto(this UiModels.TelegramUser ui) => new()
    {
        TelegramUserId = ui.TelegramUserId,
        Username = ui.Username,
        FirstName = ui.FirstName,
        LastName = ui.LastName,
        UserPhotoPath = ui.UserPhotoPath,
        PhotoHash = ui.PhotoHash,
        PhotoFileUniqueId = ui.PhotoFileUniqueId,
        IsTrusted = ui.IsTrusted,
        BotDmEnabled = ui.BotDmEnabled,
        FirstSeenAt = ui.FirstSeenAt,
        LastSeenAt = ui.LastSeenAt,
        CreatedAt = ui.CreatedAt,
        UpdatedAt = ui.UpdatedAt
    };
}
