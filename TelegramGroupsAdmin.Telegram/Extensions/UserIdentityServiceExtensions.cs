using Telegram.Bot.Types;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Services.Identity;

namespace TelegramGroupsAdmin.Telegram.Extensions;

public static class UserIdentityServiceExtensions
{
    extension(IUserIdentityService identities)
    {
        /// <summary>
        /// Observes the user a replied-to (or backfilled) message names. The names are an observation, so a
        /// user the bot never saw is recorded first; <paramref name="sentAt"/> is that message's date.
        /// Records only: a ReplyTarget observation never rescans.
        /// </summary>
        public Task<UserIdentity> ObserveReplyTargetAsync(
            User from, DateTime sentAt, ChatIdentity chat, CancellationToken cancellationToken = default) =>
            identities.ObserveAsync(
                new ObservedUser(
                    from.Id, from.FirstName, from.LastName, from.Username, from.IsBot,
                    ObservationSource.ReplyTarget, sentAt.ToUtcOffset()),
                new ProfileChangeContext(chat),
                cancellationToken);
    }
}
