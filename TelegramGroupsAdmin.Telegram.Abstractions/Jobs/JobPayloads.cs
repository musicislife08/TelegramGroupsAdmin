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

/// <summary>
/// Payload for fetch user photo job
/// Downloads and caches user profile photo, then updates message record
/// </summary>
public record FetchUserPhotoPayload(
    long MessageId,
    long UserId
);

/// <summary>
/// Payload for file scanning job (Phase 4.14)
/// Downloads file attachment, scans with ClamAV + VirusTotal, takes action if infected
/// </summary>
public record FileScanJobPayload(
    /// <summary>Message ID for audit trail and deletion if infected</summary>
    long MessageId,

    /// <summary>Chat ID where message was sent</summary>
    long ChatId,

    /// <summary>User ID who sent the file (for DM notifications)</summary>
    long UserId,

    /// <summary>Telegram file ID for download</summary>
    string FileId,

    /// <summary>File size in bytes</summary>
    long FileSize,

    /// <summary>Original file name (for logging and user notifications)</summary>
    string? FileName,

    /// <summary>MIME type (e.g., "application/pdf", "image/jpeg")</summary>
    string? ContentType
);
