using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Ui.Models;

namespace TelegramGroupsAdmin.Ui.Server.Extensions;

/// <summary>
/// Extension methods for mapping ManagedChatRecord to UI models.
/// Centralizes mapping logic used across API endpoints.
/// </summary>
public static class ManagedChatMappingExtensions
{
    /// <summary>
    /// Converts a ManagedChatRecord to a ChatSummary for UI display.
    /// </summary>
    public static ChatSummary ToChatSummary(this ManagedChatRecord chat)
        => new(
            chat.ChatId,
            chat.ChatName ?? $"Chat {chat.ChatId}",
            chat.ChatIconPath,
            0,    // TODO: Add message count query
            null  // TODO: Add last message timestamp query
        );

    /// <summary>
    /// Converts a collection of ManagedChatRecords to ChatSummaries.
    /// </summary>
    public static List<ChatSummary> ToChatSummaries(this IEnumerable<ManagedChatRecord> chats)
        => chats.Select(c => c.ToChatSummary()).ToList();
}
