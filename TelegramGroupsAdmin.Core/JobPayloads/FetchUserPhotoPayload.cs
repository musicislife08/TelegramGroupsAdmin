namespace TelegramGroupsAdmin.Core.JobPayloads;

/// <summary>
/// Payload for fetch user photo job
/// Downloads and caches user profile photo, then updates message record
/// </summary>
public record FetchUserPhotoPayload(
    int MessageId,
    long UserId
);
