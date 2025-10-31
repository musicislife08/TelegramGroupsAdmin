using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories.Mappings;

/// <summary>
/// Mapping extensions for Welcome Response records (Phase 4.4)
/// </summary>
public static class WelcomeResponseMappings
{
    public static UiModels.WelcomeResponse ToModel(this DataModels.WelcomeResponseDto data) => new(
        Id: data.Id,
        ChatId: data.ChatId,
        UserId: data.UserId,
        Username: data.Username,
        WelcomeMessageId: data.WelcomeMessageId,
        Response: (UiModels.WelcomeResponseType)data.Response,
        RespondedAt: data.RespondedAt,
        DmSent: data.DmSent,
        DmFallback: data.DmFallback,
        CreatedAt: data.CreatedAt,
        TimeoutJobId: data.TimeoutJobId
    );

    public static DataModels.WelcomeResponseDto ToDto(this UiModels.WelcomeResponse ui) => new()
    {
        Id = ui.Id,
        ChatId = ui.ChatId,
        UserId = ui.UserId,
        Username = ui.Username,
        WelcomeMessageId = ui.WelcomeMessageId,
        Response = (DataModels.WelcomeResponseType)ui.Response,
        RespondedAt = ui.RespondedAt,
        DmSent = ui.DmSent,
        DmFallback = ui.DmFallback,
        CreatedAt = ui.CreatedAt,
        TimeoutJobId = ui.TimeoutJobId
    };
}
