using Telegram.Bot.Types;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.Telegram.Extensions;

/// <summary>
/// C# 14 static extension factories for <see cref="UserIdentity"/> and <see cref="ChatIdentity"/>.
/// Adds From() overloads for SDK types, domain models, and DB-resolving async factories.
/// </summary>
public static class IdentityExtensions
{
    // ═══════════════════════════════════════════════════════════════════════════
    // Static factory extensions for UserIdentity
    // ═══════════════════════════════════════════════════════════════════════════

    extension(UserIdentity)
    {
        /// <summary>
        /// Create identity from Telegram SDK User object.
        /// </summary>
        public static UserIdentity From(User user)
            => new(user.Id, user.FirstName, user.LastName, user.Username);

        /// <summary>
        /// Create identity from domain model.
        /// </summary>
        public static UserIdentity From(TelegramUser user)
            => new(user.TelegramUserId, user.FirstName, user.LastName, user.Username);

        /// <summary>
        /// Create identity from database DTO.
        /// </summary>
        public static UserIdentity From(TelegramUserDto user)
            => new(user.TelegramUserId, user.FirstName, user.LastName, user.Username);

        /// <summary>
        /// Create identity by fetching user info from the database.
        /// Single fetch at the call site — the identity then flows through the entire handler chain
        /// without any downstream handler needing to re-fetch for logging.
        /// </summary>
        public static async Task<UserIdentity> FromAsync(
            long userId, ITelegramUserRepository repo, CancellationToken ct = default)
        {
            var user = await repo.GetByTelegramIdAsync(userId, ct);
            return user != null ? UserIdentity.From(user) : UserIdentity.FromId(userId);
        }
    }

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
            return chat?.Chat ?? ChatIdentity.FromId(chatId);
        }
    }
}
