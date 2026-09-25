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
    IBanCelebrationGifRepository gifRepository,
    ILogger<BanCelebrationDmSender> logger)
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
            // The DM was delivered; a failed cache write must not turn that into a failure.
            // Keep the id on the in-memory GIF so later sends in this fan-out still reuse it.
            gif.FileId = returned;
            try
            {
                await gifRepository.UpdateFileIdAsync(gif.Id, returned, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Failed to cache file_id for ban celebration GIF {GifId}", gif.Id);
            }
        }

        return result;
    }
}
