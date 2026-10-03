using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenameMaskFlaggedNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE configs
                SET welcome_config = jsonb_set(
                    welcome_config,
                    '{joinSecurity,profileScan}',
                    (welcome_config #> '{joinSecurity,profileScan}')
                        - 'maskExplicitUsername' - 'explicitUsernameRedactionText'
                        || CASE WHEN (welcome_config #> '{joinSecurity,profileScan}') ? 'maskExplicitUsername'
                                THEN jsonb_build_object('maskFlaggedNames', welcome_config #> '{joinSecurity,profileScan,maskExplicitUsername}')
                                ELSE '{}'::jsonb END)
                WHERE welcome_config #> '{joinSecurity,profileScan}' IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE configs
                SET welcome_config = jsonb_set(
                    welcome_config,
                    '{joinSecurity,profileScan}',
                    (welcome_config #> '{joinSecurity,profileScan}') - 'maskFlaggedNames'
                        || CASE WHEN (welcome_config #> '{joinSecurity,profileScan}') ? 'maskFlaggedNames'
                                THEN jsonb_build_object('maskExplicitUsername', welcome_config #> '{joinSecurity,profileScan,maskFlaggedNames}')
                                ELSE '{}'::jsonb END)
                WHERE welcome_config #> '{joinSecurity,profileScan}' IS NOT NULL;
                """);
        }
    }
}
