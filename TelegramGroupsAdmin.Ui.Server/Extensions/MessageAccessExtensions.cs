using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.Ui.Server.Extensions;

/// <summary>
/// Extension methods for message access validation.
/// Combines message fetching with access control check to reduce boilerplate.
/// </summary>
public static class MessageAccessExtensions
{
    /// <summary>
    /// Fetches a message and validates that the user has access to its chat.
    /// Returns the message if found and accessible, or an appropriate error result.
    /// </summary>
    /// <param name="messagesRepo">Message history repository</param>
    /// <param name="chatsRepo">Managed chats repository</param>
    /// <param name="messageId">The message ID to fetch</param>
    /// <param name="userId">The authenticated user's ID</param>
    /// <param name="permissionLevel">The user's permission level</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Tuple of (message, error). If error is not null, return it immediately.</returns>
    public static async Task<(MessageRecord? Message, IResult? Error)> GetMessageWithAccessCheckAsync(
        this IMessageHistoryRepository messagesRepo,
        IManagedChatsRepository chatsRepo,
        long messageId,
        string userId,
        PermissionLevel permissionLevel,
        CancellationToken ct)
    {
        var message = await messagesRepo.GetMessageAsync(messageId, ct);
        if (message == null)
        {
            return (null, Results.NotFound());
        }

        if (!await chatsRepo.HasChatAccessAsync(userId, permissionLevel, message.ChatId, ct))
        {
            return (null, Results.Forbid());
        }

        return (message, null);
    }
}
