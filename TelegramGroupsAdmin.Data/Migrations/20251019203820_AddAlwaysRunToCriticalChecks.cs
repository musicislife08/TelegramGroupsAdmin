using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TgSpam_PreFilterApi.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAlwaysRunToCriticalChecks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "always_run",
                table: "spam_check_configs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Add unique constraint on (chat_id, check_name) to prevent duplicates
            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX IF NOT EXISTS ix_spam_check_configs_chat_check
                ON spam_check_configs (chat_id, check_name);
            ");

            // Seed critical checks with always_run=true for global config (chat_id=0)
            // These checks will run for ALL users regardless of trust/admin status

            // 1. URL Filtering - Block malicious domains
            migrationBuilder.Sql(@"
                INSERT INTO spam_check_configs (chat_id, check_name, enabled, always_run, modified_date, modified_by)
                VALUES (0, 'UrlFiltering', true, true, NOW(), 'system')
                ON CONFLICT (chat_id, check_name)
                DO UPDATE SET always_run = true, enabled = true;
            ");

            // 2. VirusTotal - Malware/threat detection
            migrationBuilder.Sql(@"
                INSERT INTO spam_check_configs (chat_id, check_name, enabled, always_run, modified_date, modified_by)
                VALUES (0, 'VirusTotal', true, true, NOW(), 'system')
                ON CONFLICT (chat_id, check_name)
                DO UPDATE SET always_run = true, enabled = true;
            ");

            // 3. FileScanning - Placeholder for Phase 4.17 (ClamAV/YARA/AMSI)
            migrationBuilder.Sql(@"
                INSERT INTO spam_check_configs (chat_id, check_name, enabled, always_run, modified_date, modified_by)
                VALUES (0, 'FileScanning', false, true, NOW(), 'system')
                ON CONFLICT (chat_id, check_name)
                DO UPDATE SET always_run = true;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop the unique index
            migrationBuilder.Sql(@"
                DROP INDEX IF EXISTS ix_spam_check_configs_chat_check;
            ");

            // Delete seeded critical check records
            migrationBuilder.Sql(@"
                DELETE FROM spam_check_configs
                WHERE check_name IN ('UrlFiltering', 'VirusTotal', 'FileScanning')
                AND chat_id = 0
                AND modified_by = 'system';
            ");

            migrationBuilder.DropColumn(
                name: "always_run",
                table: "spam_check_configs");
        }
    }
}
