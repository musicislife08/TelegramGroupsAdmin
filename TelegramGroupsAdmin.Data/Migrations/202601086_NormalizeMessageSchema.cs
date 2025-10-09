using FluentMigrator;

namespace TelegramGroupsAdmin.Data.Migrations;

/// <summary>
/// Normalize database schema - consolidate message storage and spam detection
///
/// Changes:
/// 1. Remove expires_at from messages (cleanup service decides retention)
/// 2. Create detection_results table (replaces training_samples)
/// 3. Create user_actions table (for bans, warns, mutes)
/// 4. Migrate training_samples data to messages + detection_results
/// 5. Drop obsolete tables (training_samples, spam_checks)
///
/// Design:
/// - Messages stored once, referenced by detections/actions
/// - Cascade deletes for edits, orphaned detections remain for analytics
/// - Smart retention: keep messages referenced by detection_results/user_actions
/// </summary>
[Migration(202601086)]
public class NormalizeMessageSchema : Migration
{
    public override void Up()
    {
        // ============================================================
        // STEP 1: Remove expires_at from messages (FIRST - before inserting data)
        // ============================================================

        if (Schema.Table("messages").Column("expires_at").Exists())
        {
            Delete.Column("expires_at").FromTable("messages");
        }

        // ============================================================
        // STEP 2: Add missing columns to messages table
        // ============================================================

        if (!Schema.Table("messages").Column("photo_local_path").Exists())
        {
            Alter.Table("messages")
                .AddColumn("photo_local_path").AsString().Nullable();
        }

        if (!Schema.Table("messages").Column("photo_thumbnail_path").Exists())
        {
            Alter.Table("messages")
                .AddColumn("photo_thumbnail_path").AsString().Nullable();
        }

        // ============================================================
        // STEP 3: Create detection_results table (replaces training_samples)
        // ============================================================

        Create.Table("detection_results")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("message_id").AsInt64().NotNullable()
            .WithColumn("detected_at").AsInt64().NotNullable()
            .WithColumn("detection_source").AsString(50).NotNullable() // 'auto' | 'manual'
            .WithColumn("is_spam").AsBoolean().NotNullable()
            .WithColumn("confidence").AsInt32().Nullable()
            .WithColumn("reason").AsString().Nullable()
            .WithColumn("detection_method").AsString(100).Nullable() // 'StopWords' | 'Bayes' | etc
            .WithColumn("added_by").AsString(36).Nullable();

        // Foreign keys
        Create.ForeignKey("FK_detection_results_message_id_messages_message_id")
            .FromTable("detection_results").ForeignColumn("message_id")
            .ToTable("messages").PrimaryColumn("message_id")
            .OnDelete(System.Data.Rule.Cascade);

        Create.ForeignKey("FK_detection_results_added_by_users_id")
            .FromTable("detection_results").ForeignColumn("added_by")
            .ToTable("users").PrimaryColumn("id")
            .OnDelete(System.Data.Rule.SetNull);

        // Indexes
        Create.Index("idx_detection_results_message_id")
            .OnTable("detection_results").OnColumn("message_id");

        Create.Index("idx_detection_results_detected_at")
            .OnTable("detection_results").OnColumn("detected_at").Descending();

        Create.Index("idx_detection_results_is_spam_source")
            .OnTable("detection_results")
            .OnColumn("is_spam").Ascending()
            .OnColumn("detection_source").Ascending()
            .OnColumn("detected_at").Descending();

        // ============================================================
        // STEP 4: Create user_actions table (for bans, warns, mutes)
        // ============================================================

        Create.Table("user_actions")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("user_id").AsInt64().NotNullable()
            .WithColumn("chat_ids").AsCustom("BIGINT[]").Nullable() // NULL = all chats
            .WithColumn("action_type").AsString(50).NotNullable() // 'ban' | 'warn' | 'mute' | 'trust' | 'unban'
            .WithColumn("message_id").AsInt64().Nullable()
            .WithColumn("issued_by").AsString(36).Nullable()
            .WithColumn("issued_at").AsInt64().NotNullable()
            .WithColumn("expires_at").AsInt64().Nullable() // NULL = permanent
            .WithColumn("reason").AsString().Nullable();

        // Foreign keys
        Create.ForeignKey("FK_user_actions_message_id_messages_message_id")
            .FromTable("user_actions").ForeignColumn("message_id")
            .ToTable("messages").PrimaryColumn("message_id")
            .OnDelete(System.Data.Rule.SetNull);

        Create.ForeignKey("FK_user_actions_issued_by_users_id")
            .FromTable("user_actions").ForeignColumn("issued_by")
            .ToTable("users").PrimaryColumn("id")
            .OnDelete(System.Data.Rule.SetNull);

        // Indexes
        Create.Index("idx_user_actions_user_id")
            .OnTable("user_actions").OnColumn("user_id");

        Create.Index("idx_user_actions_issued_at")
            .OnTable("user_actions").OnColumn("issued_at").Descending();

        Create.Index("idx_user_actions_action_type")
            .OnTable("user_actions").OnColumn("action_type");

        // ============================================================
        // STEP 5: Migrate training_samples to messages + detection_results
        // ============================================================

        // Insert training samples as synthetic messages (negative IDs to avoid conflicts)
        Execute.Sql(@"
            INSERT INTO messages (
                message_id, chat_id, user_id, user_name, timestamp,
                message_text, content_hash
            )
            SELECT
                -ts.id as message_id,                    -- Negative ID for synthetic messages
                COALESCE(ts.chat_ids[1]::bigint, -1) as chat_id,  -- First chat or -1 for unknown
                -1 as user_id,                           -- Synthetic user
                'Unknown' as user_name,
                ts.added_date as timestamp,
                ts.message_text,
                MD5(ts.message_text) as content_hash
            FROM training_samples ts
            WHERE NOT EXISTS (
                SELECT 1 FROM messages m
                WHERE m.message_text = ts.message_text
                  AND m.timestamp = ts.added_date
            );
        ");

        // Insert detection_results from training_samples
        // Link to real messages if found, otherwise use synthetic message_id
        Execute.Sql(@"
            INSERT INTO detection_results (
                message_id, detected_at, detection_source, is_spam,
                confidence, added_by, detection_method
            )
            SELECT
                COALESCE(
                    (SELECT m.message_id FROM messages m
                     WHERE m.message_text = ts.message_text
                       AND m.timestamp = ts.added_date
                     LIMIT 1),
                    -ts.id  -- Use synthetic message_id if no match
                ) as message_id,
                ts.added_date as detected_at,
                ts.source as detection_source,
                ts.is_spam,
                ts.confidence_when_added as confidence,
                ts.added_by,
                CASE
                    WHEN ts.source = 'manual' THEN 'Manual'
                    ELSE 'Auto'
                END as detection_method
            FROM training_samples ts;
        ");

        // ============================================================
        // STEP 6: Drop obsolete tables
        // ============================================================

        // Drop training_samples (data migrated)
        if (Schema.Table("training_samples").Exists())
        {
            Delete.Table("training_samples");
        }

        // Drop spam_checks (replaced by detection_results)
        if (Schema.Table("spam_checks").Exists())
        {
            Delete.Table("spam_checks");
        }
    }

    public override void Down()
    {
        // Restore expires_at to messages
        if (!Schema.Table("messages").Column("expires_at").Exists())
        {
            Alter.Table("messages")
                .AddColumn("expires_at").AsInt64().NotNullable().WithDefaultValue(0);
        }

        // Recreate training_samples
        Create.Table("training_samples")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("message_text").AsString(4000).NotNullable()
            .WithColumn("is_spam").AsBoolean().NotNullable()
            .WithColumn("added_date").AsInt64().NotNullable()
            .WithColumn("source").AsString(50).NotNullable()
            .WithColumn("confidence_when_added").AsInt32().Nullable()
            .WithColumn("added_by").AsString(36).Nullable()
            .WithColumn("detection_count").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("last_detected_date").AsInt64().Nullable()
            .WithColumn("chat_ids").AsCustom("TEXT[]").Nullable();

        // Migrate detection_results back to training_samples
        Execute.Sql(@"
            INSERT INTO training_samples (
                message_text, is_spam, added_date, source,
                confidence_when_added, added_by
            )
            SELECT
                m.message_text,
                dr.is_spam,
                dr.detected_at as added_date,
                dr.detection_source as source,
                dr.confidence as confidence_when_added,
                dr.added_by
            FROM detection_results dr
            JOIN messages m ON dr.message_id = m.message_id
            WHERE m.message_id > 0;  -- Skip synthetic messages
        ");

        // Recreate spam_checks
        Create.Table("spam_checks")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("check_timestamp").AsInt64().NotNullable()
            .WithColumn("user_id").AsInt64().NotNullable()
            .WithColumn("content_hash").AsString(64).Nullable()
            .WithColumn("is_spam").AsBoolean().NotNullable()
            .WithColumn("confidence").AsInt32().NotNullable()
            .WithColumn("reason").AsString().Nullable()
            .WithColumn("check_type").AsString().NotNullable()
            .WithColumn("matched_message_id").AsInt64().Nullable();

        // Drop new tables
        Delete.Table("user_actions");
        Delete.Table("detection_results");

        // Remove new message columns
        Delete.Column("photo_local_path").FromTable("messages");
        Delete.Column("photo_thumbnail_path").FromTable("messages");
    }
}
