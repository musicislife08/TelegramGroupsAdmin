namespace TelegramGroupsAdmin.Data.Migrations;

/// <summary>
/// Frozen enriched_messages / enriched_reports definitions replayed by older migrations.
/// The un-suffixed constants are from before JoinUserIdentitiesInEnrichedViews, when both views joined
/// telegram_users directly. Migrations older than that recreate the views with
/// these so a fresh database replays unchanged; the view classes' CreateViewSql constants track the
/// current schema and join user_identities, which does not exist yet at those points in history.
/// </summary>
internal static class LegacyEnrichedViewSql
{
    /// <summary>enriched_messages joining telegram_users for author and reply-to names.</summary>
    public const string EnrichedMessages = """
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
        LEFT JOIN messages parent ON m.reply_to_message_id = parent.message_id AND m.chat_id = parent.chat_id
        LEFT JOIN telegram_users parent_user ON parent.user_id = parent_user.telegram_user_id
        LEFT JOIN message_translations t ON m.message_id = t.message_id AND m.chat_id = t.chat_id AND t.edit_id IS NULL;
        """;

    /// <summary>enriched_reports joining telegram_users for every report subject.</summary>
    public const string EnrichedReports = """
        CREATE VIEW enriched_reports AS
        SELECT
            -- Report base columns
            r.id,
            r.type,
            r.context,
            r.message_id,
            r.chat_id,
            r.report_command_message_id,
            r.reported_by_user_id,
            r.reported_by_user_name,
            r.reported_at,
            r.status,
            r.reviewed_by,
            r.reviewed_at,
            r.action_taken,
            r.admin_notes,
            r.web_user_id,

            -- Chat enrichment (all report types)
            c.chat_name,

            -- ImpersonationAlert: Suspected user (type = 1)
            suspected.telegram_user_id AS suspected_user_id,
            suspected.username AS suspected_username,
            suspected.first_name AS suspected_first_name,
            suspected.last_name AS suspected_last_name,
            suspected.user_photo_path AS suspected_photo_path,

            -- ImpersonationAlert: Target user (type = 1)
            target.telegram_user_id AS target_user_id,
            target.username AS target_username,
            target.first_name AS target_first_name,
            target.last_name AS target_last_name,
            target.user_photo_path AS target_photo_path,

            -- ExamResult: User (type = 2)
            exam_user.telegram_user_id AS exam_user_id,
            exam_user.username AS exam_username,
            exam_user.first_name AS exam_first_name,
            exam_user.last_name AS exam_last_name,
            exam_user.user_photo_path AS exam_photo_path,

            -- ProfileScanAlert: User (type = 3)
            profile_user.telegram_user_id AS profile_user_id,
            profile_user.username AS profile_username,
            profile_user.first_name AS profile_first_name,
            profile_user.last_name AS profile_last_name,
            profile_user.user_photo_path AS profile_photo_path,

            -- ContentReport: message author (type = 0)
            content_msg.user_id AS content_user_id,

            -- Reviewer (all types with web_user_id)
            reviewer.email AS reviewer_email

        FROM reports r

        -- Chat (always join)
        LEFT JOIN managed_chats c ON r.chat_id = c.chat_id

        -- ImpersonationAlert suspected user (only for type = 1)
        LEFT JOIN telegram_users suspected
            ON r.type = 1
            AND suspected.telegram_user_id = (r.context->>'suspectedUserId')::bigint

        -- ImpersonationAlert target user (only for type = 1)
        LEFT JOIN telegram_users target
            ON r.type = 1
            AND target.telegram_user_id = (r.context->>'targetUserId')::bigint

        -- ExamResult user (only for type = 2)
        LEFT JOIN telegram_users exam_user
            ON r.type = 2
            AND exam_user.telegram_user_id = (r.context->>'userId')::bigint

        -- ProfileScanAlert user (only for type = 3)
        LEFT JOIN telegram_users profile_user
            ON r.type = 3
            AND profile_user.telegram_user_id = (r.context->>'userId')::bigint

        -- ContentReport author (only for type = 0). Joins messages on its
        -- (message_id, chat_id) primary key. No telegram_users join — only the
        -- id is needed, for subject-user filtering.
        LEFT JOIN messages content_msg
            ON r.type = 0
            AND content_msg.chat_id = r.chat_id
            AND content_msg.message_id = r.message_id

        -- Reviewer (all types)
        LEFT JOIN users reviewer ON r.web_user_id = reviewer.id;
        """;

    /// <summary>
    /// enriched_messages joining user_identities with only latest_scan_explicit, as created by
    /// JoinUserIdentitiesInEnrichedViews (before AddNameVerdictInputsToUserIdentities).
    /// </summary>
    public const string EnrichedMessagesV2 = """
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
            -- User enrichment (identity from user_identities, photo from telegram_users)
            ui.username AS user_name,
            ui.first_name,
            ui.last_name,
            ui.is_bot,
            ui.latest_scan_explicit,
            u.user_photo_path,
            -- Reply enrichment (from parent message + user_identities)
            parent_user.first_name AS reply_to_first_name,
            parent_user.last_name AS reply_to_last_name,
            parent_user.username AS reply_to_username,
            parent_user.telegram_user_id AS reply_to_user_id,
            parent_user.is_bot AS reply_to_is_bot,
            parent_user.latest_scan_explicit AS reply_to_latest_scan_explicit,
            parent.message_text AS reply_to_text,
            -- Translation (from message_translations, message-only not edits)
            t.id AS translation_id,
            t.translated_text,
            t.detected_language,
            t.confidence AS translation_confidence,
            t.translated_at
        FROM messages m
        LEFT JOIN managed_chats c ON m.chat_id = c.chat_id
        LEFT JOIN user_identities ui ON m.user_id = ui.telegram_user_id
        LEFT JOIN telegram_users u ON m.user_id = u.telegram_user_id
        LEFT JOIN messages parent ON m.reply_to_message_id = parent.message_id AND m.chat_id = parent.chat_id
        LEFT JOIN user_identities parent_user ON parent.user_id = parent_user.telegram_user_id
        LEFT JOIN message_translations t ON m.message_id = t.message_id AND m.chat_id = t.chat_id AND t.edit_id IS NULL;
        """;

    /// <summary>
    /// enriched_reports joining user_identities with only latest_scan_explicit, as created by
    /// JoinUserIdentitiesInEnrichedViews (before AddNameVerdictInputsToUserIdentities).
    /// </summary>
    public const string EnrichedReportsV2 = """
        CREATE VIEW enriched_reports AS
        SELECT
            -- Report base columns
            r.id,
            r.type,
            r.context,
            r.message_id,
            r.chat_id,
            r.report_command_message_id,
            r.reported_by_user_id,
            r.reported_by_user_name,
            r.reported_at,
            r.status,
            r.reviewed_by,
            r.reviewed_at,
            r.action_taken,
            r.admin_notes,
            r.web_user_id,

            -- Chat enrichment (all report types)
            c.chat_name,

            -- ImpersonationAlert: Suspected user (type = 1)
            suspected_ident.telegram_user_id AS suspected_user_id,
            suspected_ident.username AS suspected_username,
            suspected_ident.first_name AS suspected_first_name,
            suspected_ident.last_name AS suspected_last_name,
            suspected_ident.is_bot AS suspected_is_bot,
            suspected_ident.latest_scan_explicit AS suspected_latest_scan_explicit,
            suspected.user_photo_path AS suspected_photo_path,

            -- ImpersonationAlert: Target user (type = 1)
            target_ident.telegram_user_id AS target_user_id,
            target_ident.username AS target_username,
            target_ident.first_name AS target_first_name,
            target_ident.last_name AS target_last_name,
            target_ident.is_bot AS target_is_bot,
            target_ident.latest_scan_explicit AS target_latest_scan_explicit,
            target.user_photo_path AS target_photo_path,

            -- ExamResult: User (type = 2)
            exam_user_ident.telegram_user_id AS exam_user_id,
            exam_user_ident.username AS exam_username,
            exam_user_ident.first_name AS exam_first_name,
            exam_user_ident.last_name AS exam_last_name,
            exam_user_ident.is_bot AS exam_user_is_bot,
            exam_user_ident.latest_scan_explicit AS exam_user_latest_scan_explicit,
            exam_user.user_photo_path AS exam_photo_path,

            -- ProfileScanAlert: User (type = 3)
            profile_user_ident.telegram_user_id AS profile_user_id,
            profile_user_ident.username AS profile_username,
            profile_user_ident.first_name AS profile_first_name,
            profile_user_ident.last_name AS profile_last_name,
            profile_user_ident.is_bot AS profile_user_is_bot,
            profile_user_ident.latest_scan_explicit AS profile_user_latest_scan_explicit,
            profile_user.user_photo_path AS profile_photo_path,

            -- ContentReport: message author (type = 0)
            content_msg.user_id AS content_user_id,

            -- Reviewer (all types with web_user_id)
            reviewer.email AS reviewer_email

        FROM reports r

        -- Chat (always join)
        LEFT JOIN managed_chats c ON r.chat_id = c.chat_id

        -- ImpersonationAlert suspected user (only for type = 1)
        LEFT JOIN user_identities suspected_ident
            ON r.type = 1
            AND suspected_ident.telegram_user_id = (r.context->>'suspectedUserId')::bigint
        LEFT JOIN telegram_users suspected
            ON suspected.telegram_user_id = suspected_ident.telegram_user_id

        -- ImpersonationAlert target user (only for type = 1)
        LEFT JOIN user_identities target_ident
            ON r.type = 1
            AND target_ident.telegram_user_id = (r.context->>'targetUserId')::bigint
        LEFT JOIN telegram_users target
            ON target.telegram_user_id = target_ident.telegram_user_id

        -- ExamResult user (only for type = 2)
        LEFT JOIN user_identities exam_user_ident
            ON r.type = 2
            AND exam_user_ident.telegram_user_id = (r.context->>'userId')::bigint
        LEFT JOIN telegram_users exam_user
            ON exam_user.telegram_user_id = exam_user_ident.telegram_user_id

        -- ProfileScanAlert user (only for type = 3)
        LEFT JOIN user_identities profile_user_ident
            ON r.type = 3
            AND profile_user_ident.telegram_user_id = (r.context->>'userId')::bigint
        LEFT JOIN telegram_users profile_user
            ON profile_user.telegram_user_id = profile_user_ident.telegram_user_id

        -- ContentReport author (only for type = 0). Joins messages on its
        -- (message_id, chat_id) primary key. No telegram_users join — only the
        -- id is needed, for subject-user filtering.
        LEFT JOIN messages content_msg
            ON r.type = 0
            AND content_msg.chat_id = r.chat_id
            AND content_msg.message_id = r.message_id

        -- Reviewer (all types)
        LEFT JOIN users reviewer ON r.web_user_id = reviewer.id;
        """;

    /// <summary>
    /// enriched_messages with the author's verdict inputs (latest_scan_promotional, is_banned) and the
    /// reply-to user's is_bot / latest_scan_explicit, as created by AddNameVerdictInputsToUserIdentities
    /// (before RemoveUnusedReplyColumnsFromEnrichedMessages).
    /// </summary>
    public const string EnrichedMessagesV3 = """
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
            -- User enrichment (identity from user_identities, photo from telegram_users)
            ui.username AS user_name,
            ui.first_name,
            ui.last_name,
            ui.is_bot,
            ui.latest_scan_explicit,
            ui.latest_scan_promotional,
            ui.is_banned,
            u.user_photo_path,
            -- Reply enrichment (from parent message + user_identities)
            parent_user.first_name AS reply_to_first_name,
            parent_user.last_name AS reply_to_last_name,
            parent_user.username AS reply_to_username,
            parent_user.telegram_user_id AS reply_to_user_id,
            parent_user.is_bot AS reply_to_is_bot,
            parent_user.latest_scan_explicit AS reply_to_latest_scan_explicit,
            parent.message_text AS reply_to_text,
            -- Translation (from message_translations, message-only not edits)
            t.id AS translation_id,
            t.translated_text,
            t.detected_language,
            t.confidence AS translation_confidence,
            t.translated_at
        FROM messages m
        LEFT JOIN managed_chats c ON m.chat_id = c.chat_id
        LEFT JOIN user_identities ui ON m.user_id = ui.telegram_user_id
        LEFT JOIN telegram_users u ON m.user_id = u.telegram_user_id
        LEFT JOIN messages parent ON m.reply_to_message_id = parent.message_id AND m.chat_id = parent.chat_id
        LEFT JOIN user_identities parent_user ON parent.user_id = parent_user.telegram_user_id
        LEFT JOIN message_translations t ON m.message_id = t.message_id AND m.chat_id = t.chat_id AND t.edit_id IS NULL;
        """;
}
