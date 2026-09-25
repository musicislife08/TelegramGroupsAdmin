using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;

namespace TelegramGroupsAdmin.Services.Notifications;

/// <summary>
/// Sends one ban celebration animation DM (chat name header + caption) and keeps the GIF's
/// cached Telegram file_id current — the single place both the subscriber fan-out and the
/// banned-user DM go through.
/// </summary>
internal sealed class BanCelebrationDmSender(
    NotificationDmDispatcher dispatcher,
    IBanCelebrationGifRepository gifRepository)
{
    public async Task<DmDeliveryResult> SendAsync(
        UserIdentity recipient,
        ChatIdentity chat,
        string caption,
        BanCelebrationGif gif,
        CancellationToken ct)
    {
        var payload = NotificationPayloadBuilder.Create(chat.ChatName ?? chat.Id.ToString())
            .WithText(caption)
            .WithAnimation(gifRepository.GetFullPath(gif.FilePath), gif.FileId)
            .Build();

        var result = await dispatcher.DispatchAsync(recipient, payload, keyboard: null, ct);

        if (result.AnimationFileId is { } returned && returned != gif.FileId)
        {
            await gifRepository.UpdateFileIdAsync(gif.Id, returned, ct);
            gif.FileId = returned;
        }

        return result;
    }
}
