using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Telegram.Metrics;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.DmCelebrations;

namespace TelegramGroupsAdmin.Services.Notifications;

/// <summary>
/// Delivers one queued celebration to every subscriber who can receive DMs, one at a time,
/// paced under Telegram's global rate limit. Subscribers are resolved now, not at enqueue time,
/// so anyone who left, was banned, or unsubscribed in between is skipped. The first send
/// uploads the GIF; every later send reuses the returned file_id.
/// </summary>
internal sealed class BanCelebrationFanoutProcessor(
    IBanCelebrationSubscriberRepository subscriberRepository,
    IBanCelebrationGifRepository gifRepository,
    IBanCelebrationSubscriptionService subscriptionService,
    BanCelebrationDmSender sender,
    PipelineMetrics pipelineMetrics,
    ILogger<BanCelebrationFanoutProcessor> logger)
{
    private static readonly TimeSpan SendSpacing = TimeSpan.FromMilliseconds(50);

    public async Task ProcessAsync(BanCelebrationFanoutItem item, CancellationToken ct)
    {
        var subscribers = await subscriberRepository.GetDeliverableSubscribersAsync(item.Chat.Id, ct);
        if (subscribers.Count == 0)
        {
            return;
        }

        var gif = await gifRepository.GetByIdAsync(item.GifId, ct);
        if (gif is null)
        {
            logger.LogWarning("Ban celebration GIF {GifId} no longer exists; skipping fan-out for {Chat}",
                item.GifId, item.Chat.ToLogDebug());
            return;
        }

        int sent = 0, blocked = 0, failed = 0;
        for (var i = 0; i < subscribers.Count; i++)
        {
            if (i > 0)
            {
                await Task.Delay(SendSpacing, ct);
            }

            var subscriber = subscribers[i];
            try
            {
                var result = await sender.SendAsync(subscriber, item.Chat, item.Caption, gif, ct);
                if (result.DmSent)
                {
                    sent++;
                    pipelineMetrics.RecordBanCelebrationDm("sent");
                }
                else if (result.Blocked)
                {
                    blocked++;
                    pipelineMetrics.RecordBanCelebrationDm("blocked");
                    await subscriptionService.RemoveAllForUserAsync(subscriber, SubscriptionRemovalReason.Blocked, ct);
                }
                else
                {
                    failed++;
                    pipelineMetrics.RecordBanCelebrationDm("failed");
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                failed++;
                pipelineMetrics.RecordBanCelebrationDm("failed");
                logger.LogWarning(ex, "Ban celebration DM to {User} failed", subscriber.ToLogDebug());
            }
        }

        logger.LogInformation(
            "Ban celebration fan-out for {Chat}: sent {Sent}, blocked {Blocked}, failed {Failed}",
            item.Chat.ToLogInfo(), sent, blocked, failed);
    }
}
