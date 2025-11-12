using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories.Mappings;

/// <summary>
/// Mapping extensions for User Action records (Phase 4.19: Actor conversion)
/// </summary>
public static class UserActionMappings
{
    extension(DataModels.UserActionRecordDto data)
    {
        public UiModels.UserActionRecord ToModel(
            string? webUserEmail = null,
            string? telegramUsername = null,
            string? telegramFirstName = null) => new(
            Id: data.Id,
            UserId: data.UserId,
            ActionType: (UiModels.UserActionType)data.ActionType,
            MessageId: data.MessageId,
            IssuedBy: ActorMappings.ToActor(data.WebUserId, data.TelegramUserId, data.SystemIdentifier, webUserEmail, telegramUsername, telegramFirstName),
            IssuedAt: data.IssuedAt,
            ExpiresAt: data.ExpiresAt,
            Reason: data.Reason
        );
    }

    extension(UiModels.UserActionRecord ui)
    {
        public DataModels.UserActionRecordDto ToDto()
        {
            ActorMappings.SetActorColumns(ui.IssuedBy, out var webUserId, out var telegramUserId, out var systemIdentifier);

            return new()
            {
                Id = ui.Id,
                UserId = ui.UserId,
                ActionType = (DataModels.UserActionType)(int)ui.ActionType,
                MessageId = ui.MessageId,
                WebUserId = webUserId,
                TelegramUserId = telegramUserId,
                SystemIdentifier = systemIdentifier,
                IssuedAt = ui.IssuedAt,
                ExpiresAt = ui.ExpiresAt,
                Reason = ui.Reason
            };
        }
    }
}
