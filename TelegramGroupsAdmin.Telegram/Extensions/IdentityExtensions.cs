using Telegram.Bot.Types;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.Telegram.Extensions;

/// <summary>
/// C# 14 static extension factories for <see cref="ChatIdentity"/>. User identities come from
/// IUserIdentityService, never from here.
/// </summary>
public static class IdentityExtensions
{
    // ═══════════════════════════════════════════════════════════════════════════
    // Static factory extensions for ChatIdentity
    // ═══════════════════════════════════════════════════════════════════════════

    extension(ChatIdentity)
    {
        /// <summary>
        /// Create identity from Telegram SDK Chat object.
        /// </summary>
        public static ChatIdentity From(Chat chat) => new(chat.Id, chat.Title);

        /// <summary>
        /// Create identity by fetching chat info from the database.
        /// Single fetch at the call site — the identity then flows through the entire handler chain
        /// without any downstream handler needing to re-fetch for logging.
        /// </summary>
        public static async Task<ChatIdentity> FromAsync(
            long chatId, IManagedChatsRepository repo, CancellationToken ct = default)
        {
            var chat = await repo.GetByChatIdAsync(chatId, ct);
            return chat?.Identity ?? ChatIdentity.FromId(chatId);
        }
    }
}
