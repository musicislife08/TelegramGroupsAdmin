using TelegramGroupsAdmin.Core.Mappings;
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
        /// Maps to UI model with user details from navigation property.
        /// Includes linked web user if the Telegram admin has linked a web account.
        /// </summary>
        public UiModels.ChatAdmin ToModel()
        {
            // Get the linked web user (first active mapping's user, if any)
            var linkedWebUser = data.TelegramUser?.UserMappings
                .FirstOrDefault(m => m.IsActive)?.User?.ToModel();

            return new()
            {
                Id = data.Id,
                ChatId = data.ChatId,
                TelegramId = data.TelegramId,
                Username = data.TelegramUser?.Username,
                FirstName = data.TelegramUser?.FirstName,
                LastName = data.TelegramUser?.LastName,
                IsCreator = data.IsCreator,
                PromotedAt = data.PromotedAt,
                LastVerifiedAt = data.LastVerifiedAt,
                IsActive = data.IsActive,
                LinkedWebUser = linkedWebUser
            };
        }

        /// <summary>
        /// Maps to UI model with explicit user details (for inline projections)
        /// </summary>
        public UiModels.ChatAdmin ToModel(string? username, string? firstName, string? lastName) => new()
        {
            Id = data.Id,
            ChatId = data.ChatId,
            TelegramId = data.TelegramId,
            Username = username,
            FirstName = firstName,
            LastName = lastName,
            IsCreator = data.IsCreator,
            PromotedAt = data.PromotedAt,
            LastVerifiedAt = data.LastVerifiedAt,
            IsActive = data.IsActive
        };
    }

    extension(UiModels.ChatAdmin ui)
    {
        public DataModels.ChatAdminRecordDto ToDto() => new()
        {
            Id = ui.Id,
            ChatId = ui.ChatId,
            TelegramId = ui.TelegramId,
            IsCreator = ui.IsCreator,
            PromotedAt = ui.PromotedAt,
            LastVerifiedAt = ui.LastVerifiedAt,
            IsActive = ui.IsActive
        };
    }
}
