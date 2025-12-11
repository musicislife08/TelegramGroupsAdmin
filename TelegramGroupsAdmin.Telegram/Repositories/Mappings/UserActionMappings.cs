using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Core.Repositories.Mappings;

namespace TelegramGroupsAdmin.Telegram.Repositories.Mappings;

/// <summary>
/// Mapping extensions for User Action records (Phase 4.19: Actor conversion)
/// Enriched with target user display name from telegram_users JOIN
/// </summary>
public static class UserActionMappings
{
    extension(DataModels.UserActionRecordDto data)
    {
        /// <summary>
        /// Convert DTO to UI model with optional enrichment for IssuedBy actor and target user.
        /// </summary>
        /// <param name="webUserEmail">Email for IssuedBy if web user</param>
        /// <param name="telegramUsername">Username for IssuedBy if telegram user</param>
        /// <param name="telegramFirstName">First name for IssuedBy if telegram user</param>
        /// <param name="telegramLastName">Last name for IssuedBy if telegram user</param>
        /// <param name="targetUsername">Username of the target user (from telegram_users JOIN)</param>
        /// <param name="targetFirstName">First name of the target user (from telegram_users JOIN)</param>
        /// <param name="targetLastName">Last name of the target user (from telegram_users JOIN)</param>
        public UiModels.UserActionRecord ToModel(
            string? webUserEmail = null,
            string? telegramUsername = null,
            string? telegramFirstName = null,
            string? telegramLastName = null,
            string? targetUsername = null,
            string? targetFirstName = null,
            string? targetLastName = null) => new(
            Id: data.Id,
            UserId: data.UserId,
            ActionType: (UiModels.UserActionType)data.ActionType,
            MessageId: data.MessageId,
            IssuedBy: ActorMappings.ToActor(data.WebUserId, data.TelegramUserId, data.SystemIdentifier, webUserEmail, telegramUsername, telegramFirstName, telegramLastName),
            IssuedAt: data.IssuedAt,
            ExpiresAt: data.ExpiresAt,
            Reason: data.Reason,
            TargetUsername: targetUsername,
            TargetFirstName: targetFirstName,
            TargetLastName: targetLastName
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
