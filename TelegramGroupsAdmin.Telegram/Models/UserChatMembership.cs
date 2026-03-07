using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Chat membership info for user detail view
/// </summary>
public class UserChatMembership
{
    public ChatIdentity Identity { get; set; } = ChatIdentity.FromId(0);
    public int MessageCount { get; set; }
    public DateTimeOffset LastActivityAt { get; set; }
    public DateTimeOffset FirstSeenAt { get; set; }
    public bool IsBanned { get; set; }
}
