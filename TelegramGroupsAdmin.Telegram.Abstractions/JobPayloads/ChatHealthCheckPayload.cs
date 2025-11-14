namespace TelegramGroupsAdmin.Telegram.Abstractions;

/// <summary>
/// Payload for ChatHealthCheckJob
/// Phase 4: Chat health monitoring optimization (Quartz.NET migration)
/// </summary>
public record ChatHealthCheckPayload
{
    /// <summary>
    /// Specific chat ID to check (null = check all chats)
    /// Used for manual UI-triggered refreshes (single chat) vs recurring job (all chats)
    /// </summary>
    public long? ChatId { get; init; }
}
