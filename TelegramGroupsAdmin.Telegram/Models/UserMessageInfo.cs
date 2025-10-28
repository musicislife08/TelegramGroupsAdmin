namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// User message info for cross-chat ban cleanup (FEATURE-4.23)
/// </summary>
public record UserMessageInfo
{
    public long MessageId { get; init; }
    public long ChatId { get; init; }
}
