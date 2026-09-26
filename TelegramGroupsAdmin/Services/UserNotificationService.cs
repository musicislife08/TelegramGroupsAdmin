using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Services.Notifications;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.Services;

/// <inheritdoc />
internal sealed class UserNotificationService(
    IBanCelebrationFanoutQueue fanoutQueue,
    IBanCelebrationGifRepository gifRepository,
    BanCelebrationDmSender celebrationSender) : IUserNotificationService
{
    public ValueTask EnqueueBanCelebrationAsync(ChatIdentity chat, string caption, int gifId, CancellationToken cancellationToken = default) =>
        fanoutQueue.EnqueueAsync(new BanCelebrationFanoutItem(chat, caption, gifId), cancellationToken);

    public async Task<bool> SendBanCelebrationToBannedUserAsync(
        ChatIdentity chat,
        UserIdentity bannedUser,
        string dmCaption,
        int gifId,
        CancellationToken cancellationToken = default)
    {
        var gif = await gifRepository.GetByIdAsync(gifId, cancellationToken);
        if (gif is null)
        {
            return false;
        }

        var result = await celebrationSender.SendAsync(bannedUser, chat, dmCaption, gif, cancellationToken);
        return result.DmSent;
    }
}
