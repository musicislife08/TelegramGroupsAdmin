using TelegramGroupsAdmin.Core.Mappings;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories.Mappings;
using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories.Mappings;

/// <summary>
/// Mapping extensions for ChatAdmin records
/// </summary>
public static class ChatAdminMappings
{
    extension(DataModels.ChatAdminRecordDto data)
    {
        /// <summary>
        /// Maps to UI model. The identity comes from the admin's user_identities row (null when the
        /// user has no telegram_users row); BotDmEnabled and the linked web user come from the
        /// TelegramUser navigation.
        /// </summary>
        public UiModels.ChatAdmin ToModel(DataModels.UserIdentityView? identity)
        {
            // Get the linked web user (first active mapping's user, if any)
            var linkedWebUser = data.TelegramUser?.UserMappings
                .FirstOrDefault(m => m.IsActive)?.User?.ToModel();

            return new()
            {
                Id = data.Id,
                ChatId = data.ChatId,
                User = identity?.ToIdentity() ?? UserIdentity.FromId(data.TelegramId),
                IsCreator = data.IsCreator,
                PromotedAt = data.PromotedAt,
                LastVerifiedAt = data.LastVerifiedAt,
                IsActive = data.IsActive,
                BotDmEnabled = data.TelegramUser?.BotDmEnabled ?? false,
                LinkedWebUser = linkedWebUser
            };
        }
    }

    extension(UiModels.ChatAdmin ui)
    {
        public DataModels.ChatAdminRecordDto ToDto() => new()
        {
            Id = ui.Id,
            ChatId = ui.ChatId,
            TelegramId = ui.User.Id,
            IsCreator = ui.IsCreator,
            PromotedAt = ui.PromotedAt,
            LastVerifiedAt = ui.LastVerifiedAt,
            IsActive = ui.IsActive
        };
    }
}
