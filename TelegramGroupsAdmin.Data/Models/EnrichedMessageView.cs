using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// View-backed entity for enriched messages with user/chat/reply/translation data.
/// Maps to enriched_messages PostgreSQL view.
/// NOTE: Named *View (not *Dto) to avoid backup/restore reflection picking this up.
/// </summary>
public class EnrichedMessageView
{
    #region View Definition SQL

    /// <summary>
    /// SQL to create the enriched_messages view. Referenced by migrations.
    /// Includes all message columns plus enrichment from:
    /// - managed_chats (chat name, icon)
    /// - telegram_users (user name, photo)
    /// - parent message + user (reply context)
    /// - message_translations (translation for original messages only)
    /// </summary>
    public const string CreateViewSql = """
        CREATE VIEW enriched_messages AS
        SELECT
            -- Message columns
            m.message_id,
            m.user_id,
            m.chat_id,
            m.timestamp,
            m.message_text,
            m.photo_file_id,
            m.photo_file_size,
            m.urls,
            m.edit_date,
            m.content_hash,
            m.photo_local_path,
            m.photo_thumbnail_path,
            m.deleted_at,
            m.deletion_source,
            m.reply_to_message_id,
            m.media_type,
            m.media_file_id,
            m.media_file_size,
            m.media_file_name,
            m.media_mime_type,
            m.media_local_path,
            m.media_duration,
            m.content_check_skip_reason,
            m.similarity_hash,
            -- Chat enrichment (from managed_chats)
            c.chat_name,
            c.chat_icon_path,
            -- User enrichment (from telegram_users)
            u.username AS user_name,
            u.first_name,
            u.last_name,
            u.user_photo_path,
            -- Reply enrichment (from parent message + user)
            parent_user.first_name AS reply_to_first_name,
            parent_user.last_name AS reply_to_last_name,
            parent_user.username AS reply_to_username,
            parent_user.telegram_user_id AS reply_to_user_id,
            parent.message_text AS reply_to_text,
            -- Translation (from message_translations, message-only not edits)
            t.id AS translation_id,
            t.translated_text,
            t.detected_language,
            t.confidence AS translation_confidence,
            t.translated_at
        FROM messages m
        LEFT JOIN managed_chats c ON m.chat_id = c.chat_id
        LEFT JOIN telegram_users u ON m.user_id = u.telegram_user_id
        LEFT JOIN messages parent ON m.reply_to_message_id = parent.message_id
        LEFT JOIN telegram_users parent_user ON parent.user_id = parent_user.telegram_user_id
        LEFT JOIN message_translations t ON m.message_id = t.message_id AND t.edit_id IS NULL;
        """;

    /// <summary>
    /// SQL to drop the enriched_messages view. Referenced by migrations.
    /// </summary>
    public const string DropViewSql = "DROP VIEW IF EXISTS enriched_messages";

    #endregion

    #region Message Columns (from messages table)

    [Column("message_id")]
    public long MessageId { get; set; }

    [Column("user_id")]
    public long UserId { get; set; }

    [Column("chat_id")]
    public long ChatId { get; set; }

    [Column("timestamp")]
    public DateTimeOffset Timestamp { get; set; }

    [Column("message_text")]
    public string? MessageText { get; set; }

    [Column("photo_file_id")]
    public string? PhotoFileId { get; set; }

    [Column("photo_file_size")]
    public int? PhotoFileSize { get; set; }

    [Column("urls")]
    public string? Urls { get; set; }

    [Column("edit_date")]
    public DateTimeOffset? EditDate { get; set; }

    [Column("content_hash")]
    public string? ContentHash { get; set; }

    [Column("photo_local_path")]
    public string? PhotoLocalPath { get; set; }

    [Column("photo_thumbnail_path")]
    public string? PhotoThumbnailPath { get; set; }

    [Column("deleted_at")]
    public DateTimeOffset? DeletedAt { get; set; }

    [Column("deletion_source")]
    public string? DeletionSource { get; set; }

    [Column("reply_to_message_id")]
    public long? ReplyToMessageId { get; set; }

    [Column("media_type")]
    public MediaType? MediaType { get; set; }

    [Column("media_file_id")]
    public string? MediaFileId { get; set; }

    [Column("media_file_size")]
    public long? MediaFileSize { get; set; }

    [Column("media_file_name")]
    public string? MediaFileName { get; set; }

    [Column("media_mime_type")]
    public string? MediaMimeType { get; set; }

    [Column("media_local_path")]
    public string? MediaLocalPath { get; set; }

    [Column("media_duration")]
    public int? MediaDuration { get; set; }

    [Column("content_check_skip_reason")]
    public ContentCheckSkipReason ContentCheckSkipReason { get; set; }

    [Column("similarity_hash")]
    public long? SimilarityHash { get; set; }

    #endregion

    #region Chat Enrichment (from managed_chats JOIN)

    [Column("chat_name")]
    public string? ChatName { get; set; }

    [Column("chat_icon_path")]
    public string? ChatIconPath { get; set; }

    #endregion

    #region User Enrichment (from telegram_users JOIN)

    [Column("user_name")]
    public string? UserName { get; set; }

    [Column("first_name")]
    public string? FirstName { get; set; }

    [Column("last_name")]
    public string? LastName { get; set; }

    [Column("user_photo_path")]
    public string? UserPhotoPath { get; set; }

    #endregion

    #region Reply Enrichment (from parent message + parent user JOINs)

    [Column("reply_to_first_name")]
    public string? ReplyToFirstName { get; set; }

    [Column("reply_to_last_name")]
    public string? ReplyToLastName { get; set; }

    [Column("reply_to_username")]
    public string? ReplyToUsername { get; set; }

    [Column("reply_to_user_id")]
    public long? ReplyToUserId { get; set; }

    [Column("reply_to_text")]
    public string? ReplyToText { get; set; }

    #endregion

    #region Translation (from message_translations, message-only not edits)

    [Column("translation_id")]
    public long? TranslationId { get; set; }

    [Column("translated_text")]
    public string? TranslatedText { get; set; }

    [Column("detected_language")]
    public string? DetectedLanguage { get; set; }

    [Column("translation_confidence")]
    public decimal? TranslationConfidence { get; set; }

    [Column("translated_at")]
    public DateTimeOffset? TranslatedAt { get; set; }

    #endregion
}
