using Telegram.Bot.Types.ReplyMarkups;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.Bot;

namespace TelegramGroupsAdmin.Services.Notifications;

/// <summary>
/// The single path from a <see cref="NotificationPayload"/> to a Telegram DM, shared by the
/// admin- and user-facing notification services. Renders once and picks the DM overload:
/// animation, media/keyboard, or text.
/// </summary>
internal sealed class NotificationDmDispatcher(
    IBotDmService dmService,
    ITelegramUserRepository telegramUserRepository)
{
    private const string NotificationType = "notification";

    public async Task<DmDeliveryResult> DispatchAsync(
        long telegramId,
        NotificationPayload payload,
        InlineKeyboardMarkup? keyboard,
        CancellationToken ct)
    {
        var recipient = await UserIdentity.FromAsync(telegramId, telegramUserRepository, ct);
        return await DispatchAsync(recipient, payload, keyboard, ct);
    }

    public Task<DmDeliveryResult> DispatchAsync(
        UserIdentity recipient,
        NotificationPayload payload,
        InlineKeyboardMarkup? keyboard,
        CancellationToken ct)
    {
        var rendered = NotificationRenderer.ToTelegramMessage(payload);

        if (payload.Animation is { } animation)
        {
            return dmService.SendDmWithAnimationEntitiesAsync(recipient, rendered, animation.FileId, animation.Path, ct);
        }

        if (keyboard != null || !string.IsNullOrWhiteSpace(payload.PhotoPath) || !string.IsNullOrWhiteSpace(payload.VideoPath))
        {
            return dmService.SendDmWithMediaAndKeyboardEntitiesAsync(
                recipient,
                NotificationType,
                rendered.Text,
                rendered.Entities,
                photoPath: payload.PhotoPath,
                videoPath: payload.VideoPath,
                keyboard: keyboard,
                cancellationToken: ct);
        }

        return dmService.SendDmWithEntitiesAsync(recipient, NotificationType, rendered.Text, rendered.Entities, cancellationToken: ct);
    }
}
