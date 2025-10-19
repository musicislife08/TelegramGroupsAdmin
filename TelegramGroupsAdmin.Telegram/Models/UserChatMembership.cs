namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Chat membership info for user detail view
/// </summary>
public class UserChatMembership
{
    public long ChatId { get; set; }
    public string? ChatName { get; set; }
    public int MessageCount { get; set; }
    public DateTimeOffset LastActivityAt { get; set; }
    public DateTimeOffset FirstSeenAt { get; set; }
    public bool IsBanned { get; set; }
}
