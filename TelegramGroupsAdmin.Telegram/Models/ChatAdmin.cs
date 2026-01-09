using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;

namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// UI model for chat admin records
/// Represents a Telegram user's admin status in a specific chat
/// User details (Username, FirstName, LastName) come from telegram_users JOIN
/// </summary>
public class ChatAdmin
{
    public long Id { get; init; }
    public long ChatId { get; init; }
    public long TelegramId { get; init; }

    // User details from telegram_users JOIN
    public string? Username { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }

    public bool IsCreator { get; init; }
    public DateTimeOffset PromotedAt { get; init; }
    public DateTimeOffset LastVerifiedAt { get; init; }
    public bool IsActive { get; init; }

    /// <summary>
    /// Web user account linked to this Telegram admin via telegram_user_mappings.
    /// Null if the Telegram user has not linked a web account.
    /// </summary>
    public UserRecord? LinkedWebUser { get; init; }

    /// <summary>
    /// Formatted display name following Telegram conventions
    /// </summary>
    public string DisplayName => TelegramDisplayName.Format(FirstName, LastName, Username, TelegramId);
}
