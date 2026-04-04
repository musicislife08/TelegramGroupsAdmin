using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories.Mappings;

public static class UsernameHistoryMappings
{
    extension(DataModels.UsernameHistoryDto data)
    {
        public UiModels.UsernameHistoryRecord ToModel() => new(
            Id: data.Id,
            UserId: data.UserId,
            Username: data.Username,
            FirstName: data.FirstName,
            LastName: data.LastName,
            RecordedAt: data.RecordedAt);
    }
}
