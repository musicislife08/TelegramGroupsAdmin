using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class StoreVerificationTokenTypeAsInt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // EF's AlterColumn cannot convert the strings. The column stays NOT NULL, so a value outside
            // the three the app ever wrote becomes NULL and fails the migration instead of being guessed.
            migrationBuilder.Sql("""
                ALTER TABLE verification_tokens
                ALTER COLUMN token_type TYPE integer
                USING CASE token_type
                    WHEN 'email_verify' THEN 0
                    WHEN 'password_reset' THEN 1
                    WHEN 'email_change' THEN 2
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE verification_tokens
                ALTER COLUMN token_type TYPE character varying(50)
                USING CASE token_type
                    WHEN 0 THEN 'email_verify'
                    WHEN 1 THEN 'password_reset'
                    WHEN 2 THEN 'email_change'
                END;
                """);
        }
    }
}
