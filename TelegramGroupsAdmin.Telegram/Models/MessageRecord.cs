namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Message record for UI display
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
    string? ReplyToText
);
