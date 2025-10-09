namespace TelegramGroupsAdmin.Models;

/// <summary>
/// UI model for chat admin records
/// Represents a Telegram user's admin status in a specific chat
/// </summary>
public class ChatAdmin
{
    public long Id { get; init; }
    public long ChatId { get; init; }
    public long TelegramId { get; init; }
    public bool IsCreator { get; init; }
    public long PromotedAt { get; init; }
    public long LastVerifiedAt { get; init; }
    public bool IsActive { get; init; }
}
