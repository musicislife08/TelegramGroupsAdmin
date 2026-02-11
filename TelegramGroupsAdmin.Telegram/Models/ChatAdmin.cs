using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// UI model for chat admin records
/// Represents a Telegram user's admin status in a specific chat
/// User details come from telegram_users JOIN, embedded as UserIdentity.
/// </summary>
public class ChatAdmin
{
    public long Id { get; init; }
    public long ChatId { get; init; }
    public UserIdentity User { get; init; } = UserIdentity.FromId(0);

    public bool IsCreator { get; init; }
    public DateTimeOffset PromotedAt { get; init; }
    public DateTimeOffset LastVerifiedAt { get; init; }
    public bool IsActive { get; init; }

    /// <summary>
    /// Web user account linked to this Telegram admin via telegram_user_mappings.
    /// Null if the Telegram user has not linked a web account.
    /// </summary>
    public UserRecord? LinkedWebUser { get; init; }
}
