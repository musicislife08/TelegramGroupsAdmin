using TelegramGroupsAdmin.Core.Models;
using DataModels = TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Core.Repositories.Mappings;

/// <summary>
/// Mapping extensions for WebNotification
/// Converts between DTO (ints) and domain model (enums)
/// </summary>
public static class WebNotificationMappings
{
    extension(DataModels.WebNotificationDto data)
    {
        public WebNotification ToModel() => new()
        {
            Id = data.Id,
            UserId = data.UserId,
            Subject = data.Subject,
            Message = data.Message,
            EventType = (NotificationEventType)data.EventType,
            IsRead = data.IsRead,
            CreatedAt = data.CreatedAt,
            ReadAt = data.ReadAt
        };
    }

    extension(WebNotification model)
    {
        public DataModels.WebNotificationDto ToDto() => new()
        {
            Id = model.Id,
            UserId = model.UserId,
            Subject = model.Subject,
            Message = model.Message,
            EventType = (int)model.EventType,
            IsRead = model.IsRead,
            CreatedAt = model.CreatedAt,
            ReadAt = model.ReadAt
        };
    }
}
