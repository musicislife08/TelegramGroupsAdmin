using System.Text.Json;
using TelegramGroupsAdmin.Core.Models;
using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories.Mappings;

/// <summary>
/// Mapping extensions for Message records
/// </summary>
public static class MessageMappings
{
    // Message mappings (requires chat and user info from JOINs)
    public static UiModels.MessageRecord ToModel(
        this DataModels.MessageRecordDto data,
        string? chatName,
        string? chatIconPath,
        string? userName,
        string? firstName,
        string? userPhotoPath,
        string? replyToUser,
        string? replyToText,
        UiModels.MessageTranslation? translation = null) => new(
        MessageId: data.MessageId,
        UserId: data.UserId,
        UserName: userName,
        FirstName: firstName,
        ChatId: data.ChatId,
        Timestamp: data.Timestamp,
        MessageText: data.MessageText,
        PhotoFileId: data.PhotoFileId,
        PhotoFileSize: data.PhotoFileSize,
        Urls: data.Urls,
        EditDate: data.EditDate,
        ContentHash: data.ContentHash,
        ChatName: chatName,
        PhotoLocalPath: data.PhotoLocalPath,
        PhotoThumbnailPath: data.PhotoThumbnailPath,
        ChatIconPath: chatIconPath,
        UserPhotoPath: userPhotoPath,
        DeletedAt: data.DeletedAt,
        DeletionSource: data.DeletionSource,
        ReplyToMessageId: data.ReplyToMessageId,
        ReplyToUser: replyToUser,
        ReplyToText: replyToText,
        // Media attachment fields (Phase 4.X) - convert Data enum to UI enum
        MediaType: data.MediaType.HasValue ? (UiModels.MediaType?)data.MediaType.Value : null,
        MediaFileId: data.MediaFileId,
        MediaFileSize: data.MediaFileSize,
        MediaFileName: data.MediaFileName,
        MediaMimeType: data.MediaMimeType,
        MediaLocalPath: data.MediaLocalPath,
        MediaDuration: data.MediaDuration,
        // Translation (Phase 4.20) - passed from repository query with LEFT JOIN
        Translation: translation,
        // Spam check skip reason - convert Data enum to UI enum
        SpamCheckSkipReason: data.SpamCheckSkipReason.ToTelegramModel()
    );

    public static DataModels.MessageRecordDto ToDto(this UiModels.MessageRecord ui) => new()
    {
        MessageId = ui.MessageId,
        UserId = ui.UserId,
        ChatId = ui.ChatId,
        Timestamp = ui.Timestamp,
        MessageText = ui.MessageText,
        PhotoFileId = ui.PhotoFileId,
        PhotoFileSize = ui.PhotoFileSize,
        Urls = ui.Urls,
        EditDate = ui.EditDate,
        ContentHash = ui.ContentHash,
        PhotoLocalPath = ui.PhotoLocalPath,
        PhotoThumbnailPath = ui.PhotoThumbnailPath,
        DeletedAt = ui.DeletedAt,
        DeletionSource = ui.DeletionSource,
        ReplyToMessageId = ui.ReplyToMessageId,
        // Media attachment fields (Phase 4.X) - convert UI enum to Data enum
        MediaType = ui.MediaType.HasValue ? (DataModels.MediaType?)ui.MediaType.Value : null,
        MediaFileId = ui.MediaFileId,
        MediaFileSize = ui.MediaFileSize,
        MediaFileName = ui.MediaFileName,
        MediaMimeType = ui.MediaMimeType,
        MediaLocalPath = ui.MediaLocalPath,
        MediaDuration = ui.MediaDuration,
        // Spam check skip reason - convert UI enum to Data enum
        SpamCheckSkipReason = ui.SpamCheckSkipReason.ToDataModel()
    };
}
