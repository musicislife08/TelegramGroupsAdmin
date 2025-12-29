namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Preview data for the most recent message in a chat.
/// Used for sidebar display in the chat list.
/// </summary>
/// <param name="Timestamp">When the message was sent</param>
/// <param name="PreviewText">Truncated message text or media type indicator (e.g., "📷 Photo")</param>
public record ChatMessagePreview(
    DateTimeOffset Timestamp,
    string PreviewText
);
