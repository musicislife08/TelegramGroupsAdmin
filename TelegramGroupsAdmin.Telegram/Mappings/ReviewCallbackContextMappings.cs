using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Mappings;

public static class ReviewCallbackContextMappings
{
    public static ReviewCallbackContext ToModel(this ReviewCallbackContextDto dto) => new(
        dto.Id,
        dto.ReviewId,
        dto.ReviewType,
        dto.ChatId,
        dto.UserId,
        dto.CreatedAt);

    public static ReviewCallbackContextDto ToDto(this ReviewCallbackContext model) => new()
    {
        Id = model.Id,
        ReviewId = model.ReviewId,
        ReviewType = model.ReviewType,
        ChatId = model.ChatId,
        UserId = model.UserId,
        CreatedAt = model.CreatedAt
    };
}
