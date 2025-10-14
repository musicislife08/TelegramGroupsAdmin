using FluentMigrator;

namespace TelegramGroupsAdmin.Data.Migrations;

/// <summary>
/// Phase 2.6 (continued): Add spam check details storage and web UI report support
///
/// Changes to detection_results:
/// - check_results JSONB: Store all individual spam check results in JSON format
/// - edit_version INT: Track which message version the checks ran on (0 = original, 1+ = edits)
///
/// Changes to reports:
/// - web_user_id: FK to users table for web-submitted reports (always populated for web UI reports)
/// - reported_by_user_id: Make nullable (NULL if user has no Telegram link, populated if they do)
/// - report_command_message_id: Make nullable (NULL for web reports, populated for Telegram /report command)
///
/// Note: If a web user HAS linked their Telegram account, we store BOTH web_user_id AND reported_by_user_id
/// This enables full audit trail and proper attribution in both systems.
///
/// Rationale:
/// - JSONB approach prevents 8x storage explosion (2000 rows/day → 250 rows/day)
/// - edit_version enables tracking spam checks across message edits
/// - Web user tracking enables proper audit trail for UI-submitted reports
/// </summary>
[Migration(202601107)]
public class AddCheckResultsAndWebReports : Migration
{
    public override void Up()
    {
        // ==========================================
        // detection_results: Add check_results JSONB
        // ==========================================

        Alter.Table("detection_results")
            .AddColumn("check_results").AsCustom("JSONB").Nullable();

        // Create GIN index for efficient JSONB queries (e.g., "show all Bayes spam flags this month")
        Execute.Sql(@"
            CREATE INDEX idx_detection_check_results
            ON detection_results USING GIN (check_results);
        ");

        // ==========================================
        // detection_results: Add edit_version
        // ==========================================

        Alter.Table("detection_results")
            .AddColumn("edit_version").AsInt32().NotNullable().WithDefaultValue(0);

        Create.Index("idx_detection_results_edit_version")
            .OnTable("detection_results")
            .OnColumn("message_id").Ascending()
            .OnColumn("edit_version").Ascending();

        // ==========================================
        // reports: Add web_user_id for UI reports
        // ==========================================

        Alter.Table("reports")
            .AddColumn("web_user_id").AsString(36).Nullable()
            .ForeignKey("FK_reports_web_user_id_users_id", "users", "id")
            .OnDelete(System.Data.Rule.SetNull);

        Create.Index("idx_reports_web_user_id")
            .OnTable("reports")
            .OnColumn("web_user_id");

        // ==========================================
        // reports: Make Telegram fields nullable
        // ==========================================

        // Allow NULL for Telegram user ID (web reports without Telegram link)
        // If user HAS linked Telegram, we store BOTH web_user_id AND reported_by_user_id
        Alter.Column("reported_by_user_id")
            .OnTable("reports")
            .AsInt64().Nullable();

        // Allow NULL for Telegram command message ID (web reports don't have command message)
        Alter.Column("report_command_message_id")
            .OnTable("reports")
            .AsInt32().Nullable();
    }

    public override void Down()
    {
        // Remove indexes
        Delete.Index("idx_detection_check_results").OnTable("detection_results");
        Delete.Index("idx_detection_results_edit_version").OnTable("detection_results");
        Delete.Index("idx_reports_web_user_id").OnTable("reports");

        // Remove foreign key (FluentMigrator handles this automatically when dropping column)
        Delete.Column("web_user_id").FromTable("reports");

        // Restore NOT NULL constraints (will fail if web reports exist)
        Alter.Column("reported_by_user_id")
            .OnTable("reports")
            .AsInt64().NotNullable();

        Alter.Column("report_command_message_id")
            .OnTable("reports")
            .AsInt32().NotNullable();

        // Remove columns
        Delete.Column("edit_version").FromTable("detection_results");
        Delete.Column("check_results").FromTable("detection_results");
    }
}
