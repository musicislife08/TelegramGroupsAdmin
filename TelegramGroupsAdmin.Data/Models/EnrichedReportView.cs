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
    /// - telegram_users (suspected/target user for ImpersonationAlert, user for ExamFailure)
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

            -- ExamFailure: User (type = 2)
            exam_user.telegram_user_id AS exam_user_id,
            exam_user.username AS exam_username,
            exam_user.first_name AS exam_first_name,
            exam_user.last_name AS exam_last_name,
            exam_user.user_photo_path AS exam_photo_path,

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

        -- ExamFailure user (only for type = 2)
        LEFT JOIN telegram_users exam_user
            ON r.type = 2
            AND exam_user.telegram_user_id = (r.context->>'userId')::bigint

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

    [Column("target_photo_path")]
    public string? TargetPhotoPath { get; set; }

    #endregion

    #region ExamFailure: User (type = 2)

    [Column("exam_user_id")]
    public long? ExamUserId { get; set; }

    [Column("exam_username")]
    public string? ExamUsername { get; set; }

    [Column("exam_first_name")]
    public string? ExamFirstName { get; set; }

    [Column("exam_last_name")]
    public string? ExamLastName { get; set; }

    [Column("exam_photo_path")]
    public string? ExamPhotoPath { get; set; }

    #endregion

    #region Reviewer Enrichment (from users JOIN)

    [Column("reviewer_email")]
    public string? ReviewerEmail { get; set; }

    #endregion
}
