using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Core.JobPayloads;

/// <summary>
/// Payload for welcome timeout job
/// Kicks user if they haven't accepted the welcome message within the configured timeout
/// </summary>
public record WelcomeTimeoutPayload(
    UserIdentity User,
    ChatIdentity Chat,
    int WelcomeMessageId
);
