using FluentMigrator;

namespace TelegramGroupsAdmin.Data.Migrations;

/// <summary>
/// Fix global config uniqueness by adding a partial unique index for NULL chat_id
///
/// Background: The existing unique constraint on chat_id allows multiple NULL values
/// because in SQL, NULL != NULL. This migration adds a PostgreSQL partial unique index
/// to ensure only ONE row with chat_id IS NULL can exist (the global config).
///
/// Also cleans up any duplicate global config rows that may have been created.
/// </summary>
[Migration(202601102)]
public class FixGlobalConfigUniqueness : Migration
{
    public override void Up()
    {
        // First, clean up any duplicate global configs (keeping the most recent)
        Execute.Sql(@"
            DELETE FROM spam_detection_configs
            WHERE chat_id IS NULL
              AND id NOT IN (
                SELECT id
                FROM spam_detection_configs
                WHERE chat_id IS NULL
                ORDER BY last_updated DESC
                LIMIT 1
              )
        ");

        // Create a partial unique index for global config (PostgreSQL specific)
        // This ensures only ONE row with chat_id IS NULL can exist
        Execute.Sql(@"
            CREATE UNIQUE INDEX idx_spam_detection_configs_global_unique
            ON spam_detection_configs ((chat_id IS NULL))
            WHERE chat_id IS NULL
        ");
    }

    public override void Down()
    {
        Execute.Sql("DROP INDEX IF EXISTS idx_spam_detection_configs_global_unique");
    }
}
