using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories.Mappings;

/// <summary>
/// Mapping extensions for User Tag records (Phase 4.12: String-based tags with soft delete, Phase 4.19: Actor conversion)
/// </summary>
public static class UserTagMappings
{
    extension(DataModels.UserTagDto data)
    {
        public UiModels.UserTag ToModel(
            string? webUserEmail = null,
            string? telegramUsername = null,
            string? telegramFirstName = null,
            string? removedByWebUserEmail = null,
            string? removedByTelegramUsername = null,
            string? removedByTelegramFirstName = null) => new()
            {
                Id = data.Id,
                TelegramUserId = data.TelegramUserId,
                TagName = data.TagName,
                AddedBy = ActorMappings.ToActor(data.ActorWebUserId, data.ActorTelegramUserId, data.ActorSystemIdentifier, webUserEmail, telegramUsername, telegramFirstName),
                AddedAt = data.AddedAt,
                RemovedAt = data.RemovedAt,
                RemovedBy = data.RemovedAt.HasValue
                ? ActorMappings.ToActor(data.RemovedByWebUserId, data.RemovedByTelegramUserId, data.RemovedBySystemIdentifier, removedByWebUserEmail, removedByTelegramUsername, removedByTelegramFirstName)
                : null
            };
    }

    extension(UiModels.UserTag ui)
    {
        public DataModels.UserTagDto ToDto()
        {
            ActorMappings.SetActorColumns(ui.AddedBy, out var webUserId, out var telegramUserId, out var systemIdentifier);

            string? removedByWebUserId = null;
            long? removedByTelegramUserId = null;
            string? removedBySystemIdentifier = null;

            if (ui.RemovedBy != null)
            {
                ActorMappings.SetActorColumns(ui.RemovedBy, out removedByWebUserId, out removedByTelegramUserId, out removedBySystemIdentifier);
            }

            return new()
            {
                Id = ui.Id,
                TelegramUserId = ui.TelegramUserId,
                TagName = ui.TagName,
                ActorWebUserId = webUserId,
                ActorTelegramUserId = telegramUserId,
                ActorSystemIdentifier = systemIdentifier,
                AddedAt = ui.AddedAt,
                RemovedAt = ui.RemovedAt,
                RemovedByWebUserId = removedByWebUserId,
                RemovedByTelegramUserId = removedByTelegramUserId,
                RemovedBySystemIdentifier = removedBySystemIdentifier
            };
        }
    }
}
