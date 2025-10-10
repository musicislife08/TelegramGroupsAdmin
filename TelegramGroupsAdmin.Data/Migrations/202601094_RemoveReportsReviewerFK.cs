using FluentMigrator;

namespace TelegramGroupsAdmin.Data.Migrations;

/// <summary>
/// Remove FK constraint from reports.reviewed_by to users.id
/// This allows reports to be reviewed even if the reviewer is not in the local users table
/// (e.g., external admins, system actions, or deleted users)
/// </summary>
[Migration(202601094)]
public class RemoveReportsReviewerFK : Migration
{
    public override void Up()
    {
        // Drop FK constraint that references users table
        Delete.ForeignKey("fk_reports_reviewer").OnTable("reports");

        // Keep the column - just remove the FK enforcement
        // This allows flexibility in who can review reports
    }

    public override void Down()
    {
        // Restore FK constraint
        Create.ForeignKey("fk_reports_reviewer")
            .FromTable("reports").ForeignColumn("reviewed_by")
            .ToTable("users").PrimaryColumn("id")
            .OnDelete(System.Data.Rule.SetNull);
    }
}
