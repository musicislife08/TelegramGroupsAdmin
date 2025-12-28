using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.Ui.Server.Extensions;

/// <summary>
/// Extension methods for chat access validation.
/// Centralizes authorization logic to ensure consistent access control across endpoints.
/// </summary>
public static class ChatAccessExtensions
{
    /// <summary>
    /// Validates if a user has access to a specific chat.
    /// GlobalAdmin+ has access to all active managed chats; Admin only has access to chats they're a member of.
    /// Uses efficient EXISTS query instead of fetching all accessible chats.
    /// </summary>
    public static Task<bool> HasChatAccessAsync(
        this IManagedChatsRepository chatsRepo,
        string userId,
        PermissionLevel permissionLevel,
        long chatId,
        CancellationToken cancellationToken = default)
    {
        // Delegate to the efficient repository method that uses EXISTS query
        return chatsRepo.HasAccessToChatAsync(userId, permissionLevel, chatId, cancellationToken);
    }
}
