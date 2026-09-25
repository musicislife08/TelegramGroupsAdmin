using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Core.JobPayloads;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Telegram.Extensions;
using TelegramGroupsAdmin.Telegram.Metrics;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Bot;

namespace TelegramGroupsAdmin.Telegram.Services.DmCelebrations;

/// <inheritdoc />
public sealed class BanCelebrationSubscriptionService(
    IBanCelebrationSubscriberRepository subscriberRepository,
    ITelegramUserRepository telegramUserRepository,
    IManagedChatsRepository managedChatsRepository,
    IBotMessageService messageService,
    IBotUserService userService,
    IJobScheduler jobScheduler,
    PipelineMetrics pipelineMetrics,
    ILogger<BanCelebrationSubscriptionService> logger) : IBanCelebrationSubscriptionService
{
    internal const int PromptLifetimeSeconds = 60;
    private const string PromptTimeoutReason = "dmcelebrations_prompt_timeout";
    private const string PromptCleanupSource = "dmcelebrations_prompt";

    public async Task<DmCelebrationSubscribeResult> SubscribeAsync(ChatIdentity chat, UserIdentity user, CancellationToken ct = default)
    {
        // Group commands run before the message pipeline upserts the sender, so a member who has
        // never posted has no telegram_users row yet; the subscription row's FK needs one.
        var telegramUser = await telegramUserRepository.GetOrCreateAsync(user, isBot: false, ct);

        if (await subscriberRepository.UpsertAsync(user.Id, chat.Id, ct))
        {
            pipelineMetrics.RecordBanCelebrationSubscription("subscribe");
            logger.LogInformation("{User} subscribed to DM ban celebrations from {Chat}", user.ToLogInfo(), chat.ToLogInfo());
        }

        if (telegramUser.BotDmEnabled)
        {
            return DmCelebrationSubscribeResult.Subscribed;
        }

        await PostStartPromptAsync(chat, user, ct);
        return DmCelebrationSubscribeResult.AwaitingStart;
    }

    public async Task<bool> UnsubscribeAsync(ChatIdentity chat, UserIdentity user, CancellationToken ct = default)
    {
        var row = await subscriberRepository.GetAsync(user.Id, chat.Id, ct);
        if (row is null)
        {
            return false;
        }

        await CleanupPromptAsync(row, ct);
        await subscriberRepository.DeleteAsync(user.Id, chat.Id, ct);
        pipelineMetrics.RecordBanCelebrationSubscription("unsubscribe");
        logger.LogInformation("{User} unsubscribed from DM ban celebrations in {Chat}", user.ToLogInfo(), chat.ToLogInfo());
        return true;
    }

    public async Task<bool> IsSubscribedAsync(long chatId, long userId, CancellationToken ct = default) =>
        await subscriberRepository.GetAsync(userId, chatId, ct) is not null;

    public async Task<ChatIdentity?> ConfirmFromStartAsync(long chatId, UserIdentity user, CancellationToken ct = default)
    {
        var row = await subscriberRepository.GetAsync(user.Id, chatId, ct);
        if (row is null)
        {
            return null;
        }

        await CleanupPromptAsync(row, ct);
        return await ChatIdentity.FromAsync(chatId, managedChatsRepository, ct);
    }

    public async Task HandleChatMemberUpdateAsync(ChatMemberUpdated update, CancellationToken ct = default)
    {
        if (update.Chat.Type == ChatType.Private)
        {
            return;
        }

        var left = update.NewChatMember.Status is ChatMemberStatus.Left or ChatMemberStatus.Kicked
            || update.NewChatMember is ChatMemberRestricted { IsMember: false };
        if (!left)
        {
            return;
        }

        var user = update.NewChatMember.User;
        if (await subscriberRepository.DeleteAsync(user.Id, update.Chat.Id, ct))
        {
            pipelineMetrics.RecordBanCelebrationSubscription("left");
            logger.LogInformation(
                "Removed DM ban celebration subscription for {User} who left {Chat}",
                user.ToLogInfo(), update.Chat.ToLogInfo());
        }
    }

    public async Task HandleBotMembershipUpdateAsync(ChatMemberUpdated update, CancellationToken ct = default)
    {
        if (update.Chat.Type != ChatType.Private || update.NewChatMember.Status != ChatMemberStatus.Kicked)
        {
            return;
        }

        var user = UserIdentity.From(update.From);
        await telegramUserRepository.DisableBotDmAsync(user.Id, ct);
        await RemoveAllForUserAsync(user, SubscriptionRemovalReason.Blocked, ct);
    }

    public async Task RemoveAllForUserAsync(UserIdentity user, SubscriptionRemovalReason reason, CancellationToken ct = default)
    {
        var removed = await subscriberRepository.DeleteAllForUserAsync(user.Id, ct);
        if (removed == 0)
        {
            return;
        }

        pipelineMetrics.RecordBanCelebrationSubscription(reason.ToString().ToLowerInvariant(), removed);
        logger.LogInformation(
            "Removed {Count} DM ban celebration subscription(s) for {User} ({Reason})",
            removed, user.ToLogInfo(), reason);
    }

    private async Task PostStartPromptAsync(ChatIdentity chat, UserIdentity user, CancellationToken ct)
    {
        // One prompt per (user, chat): close any prompt a previous /dmcelebrations on left open.
        var existing = await subscriberRepository.GetAsync(user.Id, chat.Id, ct);
        if (existing is not null)
        {
            await CleanupPromptAsync(existing, ct);
        }

        var bot = await userService.GetMeAsync(ct);
        var link = DmCelebrationDeepLink.Build(bot.Username!, chat.Id);
        var text = new TelegramMessageBuilder()
            .Mention(user)
            .Text($", tap below so I can send you {chat.ChatName ?? "this chat"}'s ban celebrations.")
            .Build();
        var keyboard = new InlineKeyboardMarkup(InlineKeyboardButton.WithUrl("🎉 Open a chat with me", link));

        var prompt = await messageService.SendAndSaveMessageAsync(chat.Id, text, replyMarkup: keyboard, cancellationToken: ct);
        var jobId = await jobScheduler.ScheduleJobAsync(
            BackgroundJobNames.DeleteMessage,
            new DeleteMessagePayload(chat.Id, prompt.MessageId, PromptTimeoutReason),
            PromptLifetimeSeconds,
            DeduplicationKeys.None,
            ct);

        await subscriberRepository.SetPromptAsync(user.Id, chat.Id, prompt.MessageId, jobId, ct);
    }

    /// <summary>
    /// Cancels the pending delete job, deletes the prompt, and always clears the columns. A job that
    /// already ran skips the delete; a message that is already gone is logged and treated as success.
    /// </summary>
    private async Task CleanupPromptAsync(BanCelebrationSubscriber row, CancellationToken ct)
    {
        if (row.PromptMessageId is null && row.PromptDeleteJobId is null)
        {
            return;
        }

        // A job that could not be cancelled has already run and deleted the prompt itself.
        var promptStillPosted = row.PromptDeleteJobId is not { } jobId
            || await jobScheduler.CancelJobAsync(jobId, ct);

        if (promptStillPosted && row.PromptMessageId is { } messageId)
        {
            try
            {
                await messageService.DeleteAndMarkMessageAsync(row.ChatId, messageId, PromptCleanupSource, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogDebug(ex, "Prompt {MessageId} in {Chat} was already gone", messageId, row.ChatId);
            }
        }

        await subscriberRepository.ClearPromptAsync(row.TelegramUserId, row.ChatId, ct);
    }
}
