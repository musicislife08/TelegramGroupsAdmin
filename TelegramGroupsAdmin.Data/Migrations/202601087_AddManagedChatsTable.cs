using FluentMigrator;

namespace TelegramGroupsAdmin.Data.Migrations;

/// <summary>
/// Add managed_chats table and normalize user_actions enum storage
///
/// Changes:
/// 1. Create managed_chats table to track bot's chat membership
/// 2. Alter user_actions.action_type from TEXT to INT for consistency
///
/// Purpose:
/// - Track bot's membership status in chats (member, admin, left, kicked)
/// - Store per-chat configuration and settings
/// - Enable cross-chat ban enforcement (ban from all active chats)
/// - Provide health checks for command execution
/// - Standardize enum storage across all tables (use INT not TEXT)
///
/// Design:
/// - Never delete rows (soft delete via is_active flag)
/// - Preserve settings when bot rejoins a chat
/// - Track admin status for permission checks
/// - Enums stored as INT for efficiency and type safety
/// </summary>
[Migration(202601087)]
public class AddManagedChatsTable : Migration
{
    public override void Up()
    {
        // ============================================================
        // STEP 1: Alter user_actions.action_type from TEXT to INT
        // ============================================================

        // Convert existing TEXT values to INT
        // 'ban' -> 0, 'warn' -> 1, 'mute' -> 2, 'trust' -> 3, 'unban' -> 4
        Execute.Sql(@"
            ALTER TABLE user_actions
            ALTER COLUMN action_type TYPE INT
            USING CASE action_type
                WHEN 'ban' THEN 0
                WHEN 'warn' THEN 1
                WHEN 'mute' THEN 2
                WHEN 'trust' THEN 3
                WHEN 'unban' THEN 4
                ELSE 0
            END;
        ");

        // ============================================================
        // STEP 2: Create managed_chats table
        // ============================================================

        Create.Table("managed_chats")
            .WithColumn("chat_id").AsInt64().PrimaryKey()
            .WithColumn("chat_name").AsString().Nullable()
            .WithColumn("chat_type").AsInt32().NotNullable()           // ManagedChatType enum: 0=Private, 1=Group, 2=Supergroup, 3=Channel
            .WithColumn("bot_status").AsInt32().NotNullable()          // BotChatStatus enum: 0=Member, 1=Administrator, 2=Left, 3=Kicked
            .WithColumn("is_admin").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("added_at").AsInt64().NotNullable()
            .WithColumn("is_active").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("last_seen_at").AsInt64().Nullable()
            .WithColumn("settings_json").AsString().Nullable();        // Future: per-chat spam detection config

        // Index for querying active chats
        Create.Index("idx_managed_chats_active")
            .OnTable("managed_chats")
            .OnColumn("is_active")
            .Ascending();

        // Partial index for querying admin chats (only active chats)
        Execute.Sql(@"
            CREATE INDEX idx_managed_chats_admin
            ON managed_chats (is_admin)
            WHERE is_active = true;
        ");
    }

    public override void Down()
    {
        Delete.Table("managed_chats");

        // Revert user_actions.action_type from INT back to TEXT
        Execute.Sql(@"
            ALTER TABLE user_actions
            ALTER COLUMN action_type TYPE VARCHAR(50)
            USING CASE action_type
                WHEN 0 THEN 'ban'
                WHEN 1 THEN 'warn'
                WHEN 2 THEN 'mute'
                WHEN 3 THEN 'trust'
                WHEN 4 THEN 'unban'
                ELSE 'ban'
            END;
        ");
    }
}
