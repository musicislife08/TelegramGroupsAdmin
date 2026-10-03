using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories;

/// <summary>
/// Repository for DM ban celebration opt-ins. The only component that touches the
/// ban_celebration_subscribers table. "Deliverable" always means the subscriber's
/// telegram_users.bot_dm_enabled is true and is_banned is false — the join lives here, never in callers.
/// </summary>
public interface IBanCelebrationSubscriberRepository
{
    /// <summary>Creates the (user, chat) row if absent. Returns true when a row was created.</summary>
    Task<bool> UpsertAsync(long telegramUserId, long chatId, CancellationToken ct = default);

    Task<BanCelebrationSubscriber?> GetAsync(long telegramUserId, long chatId, CancellationToken ct = default);

    /// <summary>Deletes the (user, chat) row. Returns true when a row existed.</summary>
    Task<bool> DeleteAsync(long telegramUserId, long chatId, CancellationToken ct = default);

    /// <summary>Deletes every row for the user. Returns the number of rows removed.</summary>
    Task<int> DeleteAllForUserAsync(long telegramUserId, CancellationToken ct = default);

    /// <summary>True when the chat has at least one deliverable subscriber (bot DMs enabled, not banned).</summary>
    Task<bool> HasDeliverableSubscribersAsync(long chatId, CancellationToken ct = default);

    /// <summary>
    /// Ids of the chat's deliverable subscribers (bot DMs enabled, not banned), oldest subscription
    /// first. Callers resolve identities through IUserIdentityService.
    /// </summary>
    Task<List<long>> GetDeliverableSubscriberIdsAsync(long chatId, CancellationToken ct = default);

    Task SetPromptAsync(long telegramUserId, long chatId, int promptMessageId, string promptDeleteJobId, CancellationToken ct = default);

    Task ClearPromptAsync(long telegramUserId, long chatId, CancellationToken ct = default);
}
