using FluentMigrator;

namespace TgSpam_PreFilterApi.Data.Migrations;

[Migration(202510063)]
public class IdentitySchema : Migration
{
    public override void Up()
    {
        // Users table
        Create.Table("users")
            .WithColumn("id").AsString(36).PrimaryKey() // UUID
            .WithColumn("email").AsString(256).NotNullable().Unique()
            .WithColumn("normalized_email").AsString(256).NotNullable()
            .WithColumn("password_hash").AsString(256).NotNullable()
            .WithColumn("security_stamp").AsString(36).NotNullable() // For invalidating sessions
            .WithColumn("permission_level").AsInt32().NotNullable().WithDefaultValue(0) // 0=ReadOnly, 1=Admin, 2=Owner
            .WithColumn("invited_by").AsString(36).Nullable()
            .WithColumn("is_active").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("totp_secret").AsString(64).Nullable() // Base32 encoded TOTP secret
            .WithColumn("totp_enabled").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("created_at").AsInt64().NotNullable()
            .WithColumn("last_login_at").AsInt64().Nullable();

        Create.Index("idx_users_normalized_email").OnTable("users")
            .OnColumn("normalized_email").Ascending();
        Create.Index("idx_users_permission_level").OnTable("users")
            .OnColumn("permission_level").Ascending();
        Create.Index("idx_users_is_active").OnTable("users")
            .OnColumn("is_active").Ascending();

        // Recovery codes table (for 2FA backup)
        Create.Table("recovery_codes")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("user_id").AsString(36).NotNullable()
                .ForeignKey("users", "id")
            .WithColumn("code_hash").AsString(256).NotNullable()
            .WithColumn("used_at").AsInt64().Nullable();

        Create.Index("idx_recovery_codes_user").OnTable("recovery_codes")
            .OnColumn("user_id").Ascending();

        // Invites table
        Create.Table("invites")
            .WithColumn("token").AsString(36).PrimaryKey() // UUID
            .WithColumn("created_by").AsString(36).NotNullable()
                .ForeignKey("users", "id")
            .WithColumn("created_at").AsInt64().NotNullable()
            .WithColumn("expires_at").AsInt64().NotNullable()
            .WithColumn("used_by").AsString(36).Nullable()
                .ForeignKey("users", "id")
            .WithColumn("used_at").AsInt64().Nullable();

        Create.Index("idx_invites_expires").OnTable("invites")
            .OnColumn("expires_at").Ascending();
        Create.Index("idx_invites_created_by").OnTable("invites")
            .OnColumn("created_by").Ascending();
    }

    public override void Down()
    {
        Delete.Table("invites");
        Delete.Table("recovery_codes");
        Delete.Table("users");
    }
}
