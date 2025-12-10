using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Core.Repositories.Mappings;

namespace TelegramGroupsAdmin.Telegram.Repositories.Mappings;

/// <summary>
/// Mapping extensions for Admin Note records (Phase 4.12, Phase 4.19: Actor conversion)
/// </summary>
public static class AdminNoteMappings
{
    extension(DataModels.AdminNoteDto data)
    {
        public UiModels.AdminNote ToModel(
            string? webUserEmail = null,
            string? telegramUsername = null,
            string? telegramFirstName = null,
            string? telegramLastName = null) => new()
            {
                Id = data.Id,
                TelegramUserId = data.TelegramUserId,
                NoteText = data.NoteText,
                CreatedBy = ActorMappings.ToActor(data.ActorWebUserId, data.ActorTelegramUserId, data.ActorSystemIdentifier, webUserEmail, telegramUsername, telegramFirstName, telegramLastName),
                CreatedAt = data.CreatedAt,
                UpdatedAt = data.UpdatedAt,
                IsPinned = data.IsPinned
            };
    }

    extension(UiModels.AdminNote ui)
    {
        public DataModels.AdminNoteDto ToDto()
        {
            ActorMappings.SetActorColumns(ui.CreatedBy, out var webUserId, out var telegramUserId, out var systemIdentifier);

            return new()
            {
                Id = ui.Id,
                TelegramUserId = ui.TelegramUserId,
                NoteText = ui.NoteText,
                ActorWebUserId = webUserId,
                ActorTelegramUserId = telegramUserId,
                ActorSystemIdentifier = systemIdentifier,
                CreatedAt = ui.CreatedAt,
                UpdatedAt = ui.UpdatedAt,
                IsPinned = ui.IsPinned
            };
        }
    }
}
