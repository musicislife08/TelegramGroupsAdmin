using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;

namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// User action record for UI display (bans, warns, mutes, trusts)
/// All actions are global - origin chat can be tracked via MessageId
/// Phase 4.19: Now uses Actor for attribution
/// Enriched with target user display name from telegram_users JOIN
/// </summary>
public record UserActionRecord(
    long Id,
    long UserId,
    UserActionType ActionType,
    long? MessageId,
    Actor IssuedBy,
    DateTimeOffset IssuedAt,
    DateTimeOffset? ExpiresAt,
    string? Reason,
    // Target user display name fields (from telegram_users JOIN)
    string? TargetUsername = null,
    string? TargetFirstName = null,
    string? TargetLastName = null
)
{
    /// <summary>
    /// Formatted display name for the target user of the action.
    /// Uses TelegramDisplayName.Format for consistent formatting.
    /// </summary>
    public string TargetDisplayName => TelegramDisplayName.Format(TargetFirstName, TargetLastName, TargetUsername, UserId);
}
