using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Ui.Models;

/// <summary>
/// A recent moderation action for the dashboard activity feed.
/// </summary>
public record RecentActivityItem(
    long Id,
    UserActionType ActionType,
    string TargetDisplayName,
    string IssuedByDisplayName,
    DateTimeOffset IssuedAt
);
