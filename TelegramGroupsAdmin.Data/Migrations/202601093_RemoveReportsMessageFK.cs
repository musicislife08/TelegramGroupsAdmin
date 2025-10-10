using FluentMigrator;

namespace TelegramGroupsAdmin.Data.Migrations;

/// <summary>
/// Remove FK constraint from reports.message_id to messages.message_id
/// This allows reports to be created even when message history caching is disabled
/// or when messages haven't been cached yet.
/// </summary>
[Migration(202601093)]
public class RemoveReportsMessageFK : Migration
{
    public override void Up()
    {
        // Drop FK constraints that reference messages table
        Delete.ForeignKey("fk_reports_message").OnTable("reports");
        Delete.ForeignKey("fk_reports_command_message").OnTable("reports");

        // Keep the columns and indexes - just remove the FK enforcement
        // This allows reports to exist without requiring messages to be cached
    }

    public override void Down()
    {
        // Restore FK constraints
        Create.ForeignKey("fk_reports_message")
            .FromTable("reports").ForeignColumn("message_id")
            .ToTable("messages").PrimaryColumn("message_id")
            .OnDelete(System.Data.Rule.Cascade);

        Create.ForeignKey("fk_reports_command_message")
            .FromTable("reports").ForeignColumn("report_command_message_id")
            .ToTable("messages").PrimaryColumn("message_id")
            .OnDelete(System.Data.Rule.Cascade);
    }
}
