using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Core.JobPayloads;

/// <summary>
/// Payload for cross-chat ban message cleanup (FEATURE-4.23)
/// Deletes all messages from a banned user across all chats
/// </summary>
public record DeleteUserMessagesPayload
{
    public required UserIdentity User { get; init; }
}
