namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Message record for UI display
/// Phase 4.X: Added media attachment support (GIF, Video, Audio, Voice, Sticker, VideoNote, Document)
/// </summary>
public record MessageRecord(
    long MessageId,
    long UserId,
    string? UserName,
    string? FirstName,
    long ChatId,
    DateTimeOffset Timestamp,
    string? MessageText,
    string? PhotoFileId,
    int? PhotoFileSize,
    string? Urls,
    DateTimeOffset? EditDate,
    string? ContentHash,
    string? ChatName,
    string? PhotoLocalPath,
    string? PhotoThumbnailPath,
    string? ChatIconPath,
    string? UserPhotoPath,
    DateTimeOffset? DeletedAt,
    string? DeletionSource,
    long? ReplyToMessageId,
    string? ReplyToUser,
    string? ReplyToText,
    // Media attachment fields (Phase 4.X)
    MediaType? MediaType,       // Type of media attachment (Animation, Video, Audio, Voice, Sticker, VideoNote, Document)
    string? MediaFileId,        // Telegram file ID for re-downloading
    long? MediaFileSize,        // File size in bytes
    string? MediaFileName,      // Original file name (for documents)
    string? MediaMimeType,      // MIME type (e.g., "video/mp4", "audio/ogg")
    string? MediaLocalPath,     // Local storage path (e.g., "/data/media/...")
    int? MediaDuration,         // Duration in seconds (for audio/video)
    // Translation fields (Phase 4.20)
    MessageTranslation? Translation // Translation of message text (if foreign language detected)
);
