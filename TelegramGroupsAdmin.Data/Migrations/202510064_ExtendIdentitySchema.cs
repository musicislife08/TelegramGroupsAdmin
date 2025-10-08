using FluentMigrator;

namespace TelegramGroupsAdmin.Data.Migrations;

/// <summary>
/// Consolidated migration extending the identity schema with:
/// - User status tracking (Pending/Active/Disabled/Deleted)
/// - Audit trail (modified_by, modified_at)
/// - Audit log table for system-wide event tracking
/// - Invite permission levels and status tracking
/// - Email verification system
/// - Password reset tokens
/// - TOTP setup timestamp tracking
/// </summary>
[Migration(202510064)]
public class ExtendIdentitySchema : Migration
{
    public override void Up()
    {
        // ===== USERS TABLE EXTENSIONS =====

        // Add status column (0=Pending, 1=Active, 2=Disabled, 3=Deleted)
        Alter.Table("users")
            .AddColumn("status").AsInt32().NotNullable().WithDefaultValue(1) // Default to Active
            .AddColumn("modified_by").AsString(36).Nullable() // Who last modified this user
            .AddColumn("modified_at").AsInt64().Nullable() // When last modified
            .AddColumn("email_verified").AsInt64().NotNullable().WithDefaultValue(0)
            .AddColumn("email_verification_token").AsString().Nullable()
            .AddColumn("email_verification_token_expires_at").AsInt64().Nullable()
            .AddColumn("password_reset_token").AsString().Nullable()
            .AddColumn("password_reset_token_expires_at").AsInt64().Nullable()
            .AddColumn("totp_setup_started_at").AsInt64().Nullable();

        // Create indexes for users table
        Create.Index("idx_users_status").OnTable("users")
            .OnColumn("status").Ascending();
        Create.Index("idx_users_modified_at").OnTable("users")
            .OnColumn("modified_at").Descending();

        // Migrate existing data: is_active=true → Active, is_active=false → Disabled
        Execute.Sql(@"
            UPDATE users
            SET status = CASE
                WHEN is_active = 1 THEN 1
                ELSE 2
            END,
            modified_at = created_at,
            email_verified = CASE
                WHEN status = 1 THEN 1
                ELSE 0
            END;
        ");

        // ===== INVITES TABLE EXTENSIONS =====

        // Add permission_level and status tracking to invites
        Alter.Table("invites")
            .AddColumn("permission_level").AsInt32().NotNullable().WithDefaultValue(0)
            .AddColumn("status").AsInt32().NotNullable().WithDefaultValue(0) // 0=Pending, 1=Used, 2=Revoked
            .AddColumn("modified_at").AsInt64().Nullable();

        // Migrate existing invite data
        Execute.Sql(@"
            UPDATE invites
            SET status = 1,
                modified_at = used_at
            WHERE used_at IS NOT NULL;
        ");

        // Drop old used_at column (data migrated to modified_at)
        Delete.Column("used_at").FromTable("invites");

        // Create index for filtering by status
        Create.Index("idx_invites_status").OnTable("invites")
            .OnColumn("status").Ascending();

        // ===== AUDIT LOG TABLE =====

        // Create audit_log table for system-wide event tracking
        Create.Table("audit_log")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("event_type").AsInt32().NotNullable() // AuditEventType enum
            .WithColumn("timestamp").AsInt64().NotNullable()
            .WithColumn("actor_user_id").AsString(36).Nullable() // Who did it (null for system events)
            .WithColumn("target_user_id").AsString(36).Nullable() // Who was affected
            .WithColumn("value").AsString(500).Nullable(); // Context/relevant data

        // Indexes for common audit queries
        Create.Index("idx_audit_log_timestamp").OnTable("audit_log")
            .OnColumn("timestamp").Descending();
        Create.Index("idx_audit_log_actor").OnTable("audit_log")
            .OnColumn("actor_user_id").Ascending();
        Create.Index("idx_audit_log_target").OnTable("audit_log")
            .OnColumn("target_user_id").Ascending();
        Create.Index("idx_audit_log_event_type").OnTable("audit_log")
            .OnColumn("event_type").Ascending();

        // ===== VERIFICATION TOKENS TABLE =====

        // Note: This table was created but is currently unused in favor of direct columns on users table
        // Keeping for future flexibility
        Create.Table("verification_tokens")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("user_id").AsString().NotNullable()
            .WithColumn("token_type").AsString().NotNullable()
            .WithColumn("token").AsString().NotNullable().Unique()
            .WithColumn("value").AsString().Nullable()
            .WithColumn("expires_at").AsInt64().NotNullable()
            .WithColumn("created_at").AsInt64().NotNullable()
            .WithColumn("used_at").AsInt64().Nullable();

        Create.Index("idx_verification_tokens_user_id")
            .OnTable("verification_tokens")
            .OnColumn("user_id");
        Create.Index("idx_verification_tokens_token")
            .OnTable("verification_tokens")
            .OnColumn("token");
        Create.Index("idx_verification_tokens_type")
            .OnTable("verification_tokens")
            .OnColumn("token_type");
    }

    public override void Down()
    {
        // Drop tables
        Delete.Table("verification_tokens");
        Delete.Table("audit_log");

        // Drop indexes
        Delete.Index("idx_invites_status").OnTable("invites");
        Delete.Index("idx_users_modified_at").OnTable("users");
        Delete.Index("idx_users_status").OnTable("users");

        // Restore invites used_at column
        Alter.Table("invites")
            .AddColumn("used_at").AsInt64().Nullable();
        Execute.Sql(@"
            UPDATE invites
            SET used_at = modified_at
            WHERE status = 1;
        ");

        // Drop invite columns
        Delete.Column("modified_at").FromTable("invites");
        Delete.Column("status").FromTable("invites");
        Delete.Column("permission_level").FromTable("invites");

        // Drop user columns
        Delete.Column("totp_setup_started_at").FromTable("users");
        Delete.Column("password_reset_token_expires_at").FromTable("users");
        Delete.Column("password_reset_token").FromTable("users");
        Delete.Column("email_verification_token_expires_at").FromTable("users");
        Delete.Column("email_verification_token").FromTable("users");
        Delete.Column("email_verified").FromTable("users");
        Delete.Column("modified_at").FromTable("users");
        Delete.Column("modified_by").FromTable("users");
        Delete.Column("status").FromTable("users");
    }
}
