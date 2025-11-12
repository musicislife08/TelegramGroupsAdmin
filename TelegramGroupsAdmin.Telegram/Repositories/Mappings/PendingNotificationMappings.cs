using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories.Mappings;

/// <summary>
/// Mapping extensions for Pending Notification records
/// </summary>
public static class PendingNotificationMappings
{
    extension(DataModels.PendingNotificationRecord db)
    {
        public UiModels.PendingNotificationModel ToModel() => new()
        {
            Id = db.Id,
            TelegramUserId = db.TelegramUserId,
            NotificationType = db.NotificationType,
            MessageText = db.MessageText,
            CreatedAt = db.CreatedAt,
            RetryCount = db.RetryCount,
            ExpiresAt = db.ExpiresAt
        };
    }

    extension(UiModels.PendingNotificationModel ui)
    {
        public DataModels.PendingNotificationRecord ToRecord() => new()
        {
            Id = ui.Id,
            TelegramUserId = ui.TelegramUserId,
            NotificationType = ui.NotificationType,
            MessageText = ui.MessageText,
            CreatedAt = ui.CreatedAt,
            RetryCount = ui.RetryCount,
            ExpiresAt = ui.ExpiresAt
        };
    }
}
