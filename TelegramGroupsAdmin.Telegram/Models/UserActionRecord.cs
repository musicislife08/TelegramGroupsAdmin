using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// User action record for UI display (bans, warns, mutes, trusts)
/// All actions are global - origin chat can be tracked via MessageId
/// Phase 4.19: Now uses Actor for attribution
/// </summary>
public record UserActionRecord(
    long Id,
    long UserId,
    UserActionType ActionType,
    long? MessageId,
    Actor IssuedBy,
    DateTimeOffset IssuedAt,
    DateTimeOffset? ExpiresAt,
    string? Reason
);
