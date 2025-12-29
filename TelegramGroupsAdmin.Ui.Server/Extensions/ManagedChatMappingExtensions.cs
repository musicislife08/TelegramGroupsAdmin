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
    public static ChatSummary ToChatSummary(
        this ManagedChatRecord chat,
        ChatMessagePreview? preview = null)
        => new(
            chat.ChatId,
            chat.ChatName ?? $"Chat {chat.ChatId}",
            chat.ChatIconPath,
            0, // Message count not needed for sidebar display
            preview?.Timestamp,
            preview?.PreviewText
        );

    /// <summary>
    /// Converts a collection of ManagedChatRecords to ChatSummaries with last message previews.
    /// </summary>
    public static List<ChatSummary> ToChatSummaries(
        this IEnumerable<ManagedChatRecord> chats,
        Dictionary<long, ChatMessagePreview>? lastMessagePreviews = null)
    {
        return chats.Select(chat =>
        {
            ChatMessagePreview? preview = null;
            lastMessagePreviews?.TryGetValue(chat.ChatId, out preview);
            return chat.ToChatSummary(preview);
        }).ToList();
    }
}
