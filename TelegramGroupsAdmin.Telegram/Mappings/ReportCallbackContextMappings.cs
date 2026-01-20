using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Mappings;

public static class ReportCallbackContextMappings
{
    public static ReportCallbackContext ToModel(this ReportCallbackContextDto dto) => new(
        dto.Id,
        dto.ReportId,
        (ReportType)dto.ReportType,  // short → enum
        dto.ChatId,
        dto.UserId,
        dto.CreatedAt);

    public static ReportCallbackContextDto ToDto(this ReportCallbackContext model) => new()
    {
        Id = model.Id,
        ReportId = model.ReportId,
        ReportType = (short)model.ReportType,  // enum → short
        ChatId = model.ChatId,
        UserId = model.UserId,
        CreatedAt = model.CreatedAt
    };
}
