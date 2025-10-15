namespace TelegramGroupsAdmin.Telegram.Abstractions.Jobs;

/// <summary>
/// Payload for welcome timeout job
/// Kicks user if they haven't accepted the welcome message within the configured timeout
/// </summary>
public record WelcomeTimeoutPayload(
    long ChatId,
    long UserId,
    int WelcomeMessageId
);

/// <summary>
/// Payload for delete message job
/// Deletes a Telegram message after a delay (e.g., warning messages, fallback rules)
/// </summary>
public record DeleteMessagePayload(
    long ChatId,
    int MessageId,
    string Reason
);
