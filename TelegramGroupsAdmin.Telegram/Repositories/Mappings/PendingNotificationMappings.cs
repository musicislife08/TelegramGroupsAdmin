using System.Text.Json;
using TelegramGroupsAdmin.Core.Models;
using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories.Mappings;

/// <summary>
/// Mapping extensions for Pending Notification records
/// </summary>
public static class PendingNotificationMappings
{
    public static UiModels.PendingNotificationModel ToModel(this DataModels.PendingNotificationRecord db) => new()
    {
        Id = db.Id,
        TelegramUserId = db.TelegramUserId,
        NotificationType = db.NotificationType,
        MessageText = db.MessageText,
        CreatedAt = db.CreatedAt,
        RetryCount = db.RetryCount,
        ExpiresAt = db.ExpiresAt
    };

    public static DataModels.PendingNotificationRecord ToRecord(this UiModels.PendingNotificationModel ui) => new()
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
