namespace TelegramGroupsAdmin.Core.JobPayloads;

/// <summary>
/// Payload for delete message job
/// Deletes a Telegram message after a delay (e.g., warning messages, fallback rules)
/// </summary>
public record DeleteMessagePayload(
    long ChatId,
    int MessageId,
    string Reason
);
