using DataModels = TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Utilities;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories.Mappings;

/// <summary>
/// Mapping extensions for EnrichedMessageView (database view with JOINs already applied)
/// </summary>
public static class EnrichedMessageMappings
{
    extension(DataModels.EnrichedMessageView view)
    {
        /// <summary>
        /// Convert enriched message view to UI model.
        /// All enrichment data (user, chat, reply, translation) is already in the view.
        /// </summary>
        public UiModels.MessageRecord ToModel()
        {
            // Build translation if present (view includes translation columns)
            MessageTranslation? translation = view.TranslationId.HasValue
                ? new MessageTranslation(
                    Id: view.TranslationId.Value,
                    MessageId: view.MessageId,
                    EditId: null, // View only includes message translations, not edit translations
                    TranslatedText: view.TranslatedText!,
                    DetectedLanguage: view.DetectedLanguage!,
                    Confidence: view.TranslationConfidence,
                    TranslatedAt: view.TranslatedAt ?? DateTimeOffset.MinValue)
                : null;

            return new UiModels.MessageRecord(
                MessageId: view.MessageId,
                User: new UserIdentity(view.UserId, view.FirstName, view.LastName, view.UserName),
                Chat: new ChatIdentity(view.ChatId, view.ChatName),
                Timestamp: view.Timestamp,
                MessageText: view.MessageText,
                PhotoFileId: view.PhotoFileId,
                PhotoFileSize: view.PhotoFileSize,
                Urls: view.Urls,
                EditDate: view.EditDate,
                ContentHash: view.ContentHash,
                PhotoLocalPath: view.PhotoLocalPath,
                PhotoThumbnailPath: view.PhotoThumbnailPath,
                ChatIconPath: view.ChatIconPath,
                UserPhotoPath: view.UserPhotoPath,
                DeletedAt: view.DeletedAt,
                DeletionSource: view.DeletionSource,
                ReplyToMessageId: view.ReplyToMessageId,
                ReplyToUser: TelegramDisplayName.Format(
                    view.ReplyToFirstName,
                    view.ReplyToLastName,
                    view.ReplyToUsername,
                    view.ReplyToUserId),
                ReplyToText: view.ReplyToText,
                // Media attachment fields - convert Data enum to UI enum
                MediaType: view.MediaType.HasValue ? (UiModels.MediaType?)view.MediaType.Value : null,
                MediaFileId: view.MediaFileId,
                MediaFileSize: view.MediaFileSize,
                MediaFileName: view.MediaFileName,
                MediaMimeType: view.MediaMimeType,
                MediaLocalPath: view.MediaLocalPath,
                MediaDuration: view.MediaDuration,
                // Translation from view columns
                Translation: translation,
                // Content check skip reason - convert Data enum to UI enum
                ContentCheckSkipReason: view.ContentCheckSkipReason.ToTelegramModel()
            );
        }
    }
}
