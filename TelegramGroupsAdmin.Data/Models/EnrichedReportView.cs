using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// View-backed entity for enriched reports with user/chat data pre-joined.
/// Maps to enriched_reports PostgreSQL view.
/// Eliminates N+1 queries by extracting JSONB user IDs and joining at the database level.
/// NOTE: Named *View (not *Dto) to avoid backup/restore reflection picking this up.
/// </summary>
public class EnrichedReportView
{
    #region View Definition SQL

    /// <summary>
    /// SQL to create the enriched_reports view. Referenced by migrations.
    /// Joins reports with all related entities based on report type:
    /// - managed_chats (chat name for all types)
    /// - user_identities (names, is_bot, latest scan flag) and telegram_users (photo only) for the
    ///   suspected/target user (ImpersonationAlert), exam user (ExamResult), profile user (ProfileScanAlert)
    /// - users (reviewer email)
    /// </summary>
    public const string CreateViewSql = """
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
    /// SQL to drop the enriched_reports view. Referenced by migrations.
    /// </summary>
    public const string DropViewSql = "DROP VIEW IF EXISTS enriched_reports";

    #endregion

    #region Report Base Columns (from reports table)

    [Column("id")]
    public long Id { get; set; }

    [Column("type")]
    public short Type { get; set; }

    [Column("context")]
    public string? Context { get; set; }

    [Column("message_id")]
    public int MessageId { get; set; }

    [Column("chat_id")]
    public long ChatId { get; set; }

    [Column("report_command_message_id")]
    public int? ReportCommandMessageId { get; set; }

    [Column("reported_by_user_id")]
    public long? ReportedByUserId { get; set; }

    [Column("reported_by_user_name")]
    public string? ReportedByUserName { get; set; }

    [Column("reported_at")]
    public DateTimeOffset ReportedAt { get; set; }

    [Column("status")]
    public int Status { get; set; }

    [Column("reviewed_by")]
    public string? ReviewedBy { get; set; }

    [Column("reviewed_at")]
    public DateTimeOffset? ReviewedAt { get; set; }

    [Column("action_taken")]
    public string? ActionTaken { get; set; }

    [Column("admin_notes")]
    public string? AdminNotes { get; set; }

    [Column("web_user_id")]
    public string? WebUserId { get; set; }

    #endregion

    #region Chat Enrichment (from managed_chats JOIN)

    [Column("chat_name")]
    public string? ChatName { get; set; }

    #endregion

    #region ImpersonationAlert: Suspected User (type = 1)

    [Column("suspected_user_id")]
    public long? SuspectedUserId { get; set; }

    [Column("suspected_username")]
    public string? SuspectedUsername { get; set; }

    [Column("suspected_first_name")]
    public string? SuspectedFirstName { get; set; }

    [Column("suspected_last_name")]
    public string? SuspectedLastName { get; set; }

    [Column("suspected_is_bot")]
    public bool? SuspectedIsBot { get; set; }

    /// <summary>Explicit flag from the user's latest profile scan; NULL when unscanned.</summary>
    [Column("suspected_latest_scan_explicit")]
    public bool? SuspectedLatestScanExplicit { get; set; }

    [Column("suspected_photo_path")]
    public string? SuspectedPhotoPath { get; set; }

    #endregion

    #region ImpersonationAlert: Target User (type = 1)

    [Column("target_user_id")]
    public long? TargetUserId { get; set; }

    [Column("target_username")]
    public string? TargetUsername { get; set; }

    [Column("target_first_name")]
    public string? TargetFirstName { get; set; }

    [Column("target_last_name")]
    public string? TargetLastName { get; set; }

    [Column("target_is_bot")]
    public bool? TargetIsBot { get; set; }

    /// <summary>Explicit flag from the user's latest profile scan; NULL when unscanned.</summary>
    [Column("target_latest_scan_explicit")]
    public bool? TargetLatestScanExplicit { get; set; }

    [Column("target_photo_path")]
    public string? TargetPhotoPath { get; set; }

    #endregion

    #region ExamResult: User (type = 2)

    [Column("exam_user_id")]
    public long? ExamUserId { get; set; }

    [Column("exam_username")]
    public string? ExamUsername { get; set; }

    [Column("exam_first_name")]
    public string? ExamFirstName { get; set; }

    [Column("exam_last_name")]
    public string? ExamLastName { get; set; }

    [Column("exam_user_is_bot")]
    public bool? ExamUserIsBot { get; set; }

    /// <summary>Explicit flag from the user's latest profile scan; NULL when unscanned.</summary>
    [Column("exam_user_latest_scan_explicit")]
    public bool? ExamUserLatestScanExplicit { get; set; }

    [Column("exam_photo_path")]
    public string? ExamPhotoPath { get; set; }

    #endregion

    #region ProfileScanAlert: User (type = 3)

    [Column("profile_user_id")]
    public long? ProfileUserId { get; set; }

    [Column("profile_username")]
    public string? ProfileUsername { get; set; }

    [Column("profile_first_name")]
    public string? ProfileFirstName { get; set; }

    [Column("profile_last_name")]
    public string? ProfileLastName { get; set; }

    [Column("profile_user_is_bot")]
    public bool? ProfileUserIsBot { get; set; }

    /// <summary>Explicit flag from the user's latest profile scan; NULL when unscanned.</summary>
    [Column("profile_user_latest_scan_explicit")]
    public bool? ProfileUserLatestScanExplicit { get; set; }

    [Column("profile_photo_path")]
    public string? ProfilePhotoPath { get; set; }

    #endregion

    #region ContentReport: Message Author (type = 0)

    /// <summary>
    /// ContentReport (type = 0): the reported message's author, joined from messages.
    /// Null for every other report type.
    /// </summary>
    [Column("content_user_id")]
    public long? ContentUserId { get; set; }

    #endregion

    #region Reviewer Enrichment (from users JOIN)

    [Column("reviewer_email")]
    public string? ReviewerEmail { get; set; }

    #endregion
}
