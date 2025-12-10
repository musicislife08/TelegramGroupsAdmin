namespace TelegramGroupsAdmin.Core.JobPayloads;

/// <summary>
/// Payload for welcome timeout job
/// Kicks user if they haven't accepted the welcome message within the configured timeout
/// </summary>
public record WelcomeTimeoutPayload(
    long ChatId,
    long UserId,
    int WelcomeMessageId
);
