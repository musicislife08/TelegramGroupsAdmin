using Telegram.Bot.Types.ReplyMarkups;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Identity;

namespace TelegramGroupsAdmin.Services.Notifications;

/// <summary>
/// The single path from a <see cref="NotificationPayload"/> to a Telegram DM, shared by the
/// admin- and user-facing notification services. Renders once and picks the DM overload:
/// animation, media/keyboard, or text. A DM is outside any chat, so names in it follow the
/// global "Mask flagged names" setting.
/// </summary>
internal sealed class NotificationDmDispatcher(
    IBotDmService dmService,
    IUserIdentityService identityService,
    IConfigService configService)
{
    private const string NotificationType = "notification";

    public async Task<DmDeliveryResult> DispatchAsync(
        long telegramId,
        NotificationPayload payload,
        InlineKeyboardMarkup? keyboard,
        CancellationToken ct)
    {
        var recipient = await identityService.ResolveAsync(telegramId, ct);
        return await DispatchAsync(recipient, payload, keyboard, ct);
    }

    public Task<DmDeliveryResult> DispatchAsync(
        UserIdentity recipient,
        NotificationPayload payload,
        InlineKeyboardMarkup? keyboard,
        CancellationToken ct)
    {
        if (payload.Animation is not null && keyboard is not null)
        {
            throw new InvalidOperationException("Animation notifications cannot carry an inline keyboard.");
        }

        return SendAsync(recipient, payload, keyboard, ct);
    }

    private async Task<DmDeliveryResult> SendAsync(
        UserIdentity recipient,
        NotificationPayload payload,
        InlineKeyboardMarkup? keyboard,
        CancellationToken ct)
    {
        var masking = await configService.GetNameMaskingAsync(null, ct);
        var rendered = NotificationRenderer.ToTelegramMessage(payload, masking);

        if (payload.Animation is { } animation)
        {
            return await dmService.SendDmWithAnimationEntitiesAsync(recipient, rendered, animation.FileId, animation.Path, ct);
        }

        if (keyboard != null || !string.IsNullOrWhiteSpace(payload.PhotoPath) || !string.IsNullOrWhiteSpace(payload.VideoPath))
        {
            return await dmService.SendDmWithMediaAndKeyboardEntitiesAsync(
                recipient,
                NotificationType,
                rendered.Text,
                rendered.Entities,
                photoPath: payload.PhotoPath,
                videoPath: payload.VideoPath,
                keyboard: keyboard,
                cancellationToken: ct);
        }

        return await dmService.SendDmWithEntitiesAsync(recipient, NotificationType, rendered.Text, rendered.Entities, cancellationToken: ct);
    }
}
