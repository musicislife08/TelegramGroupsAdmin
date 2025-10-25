namespace TelegramGroupsAdmin.Telegram.Abstractions;

/// <summary>
/// Payload for cross-chat ban message cleanup (FEATURE-4.23)
/// Deletes all messages from a banned user across all chats
/// </summary>
public record DeleteUserMessagesPayload
{
    public long TelegramUserId { get; init; }
}
