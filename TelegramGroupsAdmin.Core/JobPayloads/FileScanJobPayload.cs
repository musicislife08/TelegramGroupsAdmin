using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Core.JobPayloads;

/// <summary>
/// Payload for file scanning job (Phase 4.14)
/// Downloads file attachment, scans with ClamAV + VirusTotal, takes action if infected
/// </summary>
public record FileScanJobPayload(
    /// <summary>Message ID for audit trail and deletion if infected</summary>
    long MessageId,

    /// <summary>Chat where message was sent</summary>
    ChatIdentity Chat,

    /// <summary>User who sent the file (for DM notifications)</summary>
    UserIdentity User,

    /// <summary>Telegram file ID for download</summary>
    string FileId,

    /// <summary>File size in bytes</summary>
    long FileSize,

    /// <summary>Original file name (for logging and user notifications)</summary>
    string? FileName,

    /// <summary>MIME type (e.g., "application/pdf", "image/jpeg")</summary>
    string? ContentType
);
