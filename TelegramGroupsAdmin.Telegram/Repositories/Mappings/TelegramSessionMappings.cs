using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories.Mappings;

/// <summary>
/// Mapping extensions between TelegramSessionDto (data) and TelegramSession (domain).
/// </summary>
public static class TelegramSessionMappings
{
    extension(TelegramSessionDto dto)
    {
        public TelegramSession ToModel() => new()
        {
            Id = dto.Id,
            WebUserId = dto.WebUserId,
            TelegramUserId = dto.TelegramUserId,
            DisplayName = dto.DisplayName,
            SessionData = dto.SessionData,
            MemberChats = dto.MemberChats,
            IsActive = dto.IsActive,
            ConnectedAt = dto.ConnectedAt,
            LastUsedAt = dto.LastUsedAt,
            DisconnectedAt = dto.DisconnectedAt
        };
    }

    extension(TelegramSession model)
    {
        public TelegramSessionDto ToDto() => new()
        {
            Id = model.Id,
            WebUserId = model.WebUserId,
            TelegramUserId = model.TelegramUserId,
            DisplayName = model.DisplayName,
            SessionData = model.SessionData,
            MemberChats = model.MemberChats,
            IsActive = model.IsActive,
            ConnectedAt = model.ConnectedAt,
            LastUsedAt = model.LastUsedAt,
            DisconnectedAt = model.DisconnectedAt
        };
    }
}
