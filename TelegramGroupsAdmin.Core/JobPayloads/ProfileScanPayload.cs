namespace TelegramGroupsAdmin.Core.JobPayloads;

/// <summary>
/// Payload for on-demand profile scan job.
/// Triggers a full User API profile scan for the specified user.
/// </summary>
public record ProfileScanPayload(
    long UserId,
    long? ChatId
);
