using Telegram.Bot.Types;
using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Services.DmCelebrations;

/// <summary>
/// Owns every DM ban celebration subscription rule: opting in and out, the self-cleaning
/// start prompt, /start confirmation, and removal when a user leaves, is banned, or blocks the bot.
/// </summary>
public interface IBanCelebrationSubscriptionService
{
    Task<DmCelebrationSubscribeResult> SubscribeAsync(ChatIdentity chat, UserIdentity user, CancellationToken ct = default);

    /// <summary>Returns true when a subscription existed and was removed.</summary>
    Task<bool> UnsubscribeAsync(ChatIdentity chat, UserIdentity user, CancellationToken ct = default);

    Task<bool> IsSubscribedAsync(long chatId, long userId, CancellationToken ct = default);

    /// <summary>
    /// Handles /start dmcel_{chatId}: closes the open start prompt for the sender's own subscription.
    /// Returns the chat, or null when the sender has no subscription for that chat.
    /// </summary>
    Task<ChatIdentity?> ConfirmFromStartAsync(long chatId, UserIdentity user, CancellationToken ct = default);

    /// <summary>ChatMember updates in groups: leaving or being kicked removes that chat's subscription.</summary>
    Task HandleChatMemberUpdateAsync(ChatMemberUpdated update, CancellationToken ct = default);

    /// <summary>MyChatMember updates in private chats: blocking the bot removes every subscription.</summary>
    Task HandleBotMembershipUpdateAsync(ChatMemberUpdated update, CancellationToken ct = default);

    Task RemoveAllForUserAsync(UserIdentity user, SubscriptionRemovalReason reason, CancellationToken ct = default);
}
