using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Mappings;

public static class ReportCallbackContextMappings
{
    public static ReportCallbackContext ToModel(this ReportCallbackContextDto dto) => new(
        dto.Id,
        dto.ReportId,
        dto.ChatId,
        dto.UserId,
        dto.CreatedAt);

    public static ReportCallbackContextDto ToDto(this ReportCallbackContext model) => new()
    {
        Id = model.Id,
        ReportId = model.ReportId,
        ChatId = model.ChatId,
        UserId = model.UserId,
        CreatedAt = model.CreatedAt
    };
}
