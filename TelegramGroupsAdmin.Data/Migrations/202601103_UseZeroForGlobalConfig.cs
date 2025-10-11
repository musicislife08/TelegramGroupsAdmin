using FluentMigrator;

namespace TelegramGroupsAdmin.Data.Migrations;

/// <summary>
/// Migration 202601103: Convert spam_detection_configs to use chat_id = '0' for global config instead of NULL
///
/// Changes:
/// 1. Update existing NULL chat_id to '0'
/// 2. Drop partial unique index for NULL
/// 3. Existing uc_spam_detection_configs_chat constraint handles uniqueness for all values including '0'
/// </summary>
[Migration(202601103)]
public class UseZeroForGlobalConfig : Migration
{
    public override void Up()
    {
        // Update existing global config (NULL) to use '0'
        Execute.Sql(@"
            UPDATE spam_detection_configs
            SET chat_id = '0'
            WHERE chat_id IS NULL;
        ");

        // Drop the partial unique index (no longer needed)
        Execute.Sql(@"
            DROP INDEX IF EXISTS idx_spam_detection_configs_global_unique;
        ");

        // The existing uc_spam_detection_configs_chat unique constraint already handles uniqueness for all chat_id values including '0'
    }

    public override void Down()
    {
        // Revert '0' back to NULL for global config
        Execute.Sql(@"
            UPDATE spam_detection_configs
            SET chat_id = NULL
            WHERE chat_id = '0';
        ");

        // Recreate the partial unique index for NULL
        Execute.Sql(@"
            CREATE UNIQUE INDEX idx_spam_detection_configs_global_unique
            ON spam_detection_configs ((chat_id IS NULL))
            WHERE chat_id IS NULL;
        ");
    }
}
