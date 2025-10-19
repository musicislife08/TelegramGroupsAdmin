using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TgSpam_PreFilterApi.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUrlFilterTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "blocklist_subscriptions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    chat_id = table.Column<long>(type: "bigint", nullable: true),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    format = table.Column<int>(type: "integer", nullable: false),
                    block_mode = table.Column<int>(type: "integer", nullable: false),
                    is_built_in = table.Column<bool>(type: "boolean", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    last_fetched = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    entry_count = table.Column<int>(type: "integer", nullable: true),
                    refresh_interval_hours = table.Column<int>(type: "integer", nullable: false),
                    web_user_id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    telegram_user_id = table.Column<long>(type: "bigint", nullable: true),
                    system_identifier = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    added_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_blocklist_subscriptions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cached_blocked_domains",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    domain = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    block_mode = table.Column<int>(type: "integer", nullable: false),
                    chat_id = table.Column<long>(type: "bigint", nullable: true),
                    source_subscription_id = table.Column<long>(type: "bigint", nullable: true),
                    first_seen = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_verified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cached_blocked_domains", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "domain_filters",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    chat_id = table.Column<long>(type: "bigint", nullable: true),
                    domain = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    filter_type = table.Column<int>(type: "integer", nullable: false),
                    block_mode = table.Column<int>(type: "integer", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    web_user_id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    telegram_user_id = table.Column<long>(type: "bigint", nullable: true),
                    system_identifier = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    added_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_domain_filters", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_blocklist_subscriptions_block_mode",
                table: "blocklist_subscriptions",
                column: "block_mode",
                filter: "block_mode > 0");

            migrationBuilder.CreateIndex(
                name: "IX_blocklist_subscriptions_chat_id",
                table: "blocklist_subscriptions",
                column: "chat_id");

            migrationBuilder.CreateIndex(
                name: "IX_blocklist_subscriptions_enabled",
                table: "blocklist_subscriptions",
                column: "enabled",
                filter: "enabled = true");

            migrationBuilder.CreateIndex(
                name: "IX_blocklist_subscriptions_url",
                table: "blocklist_subscriptions",
                column: "url");

            migrationBuilder.CreateIndex(
                name: "IX_cached_blocked_domains_domain_block_mode_chat_id",
                table: "cached_blocked_domains",
                columns: new[] { "domain", "block_mode", "chat_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cached_blocked_domains_last_verified",
                table: "cached_blocked_domains",
                column: "last_verified");

            migrationBuilder.CreateIndex(
                name: "IX_cached_blocked_domains_source_subscription_id",
                table: "cached_blocked_domains",
                column: "source_subscription_id");

            migrationBuilder.CreateIndex(
                name: "IX_domain_filters_chat_id",
                table: "domain_filters",
                column: "chat_id");

            migrationBuilder.CreateIndex(
                name: "IX_domain_filters_domain",
                table: "domain_filters",
                column: "domain");

            migrationBuilder.CreateIndex(
                name: "IX_domain_filters_filter_type_block_mode",
                table: "domain_filters",
                columns: new[] { "filter_type", "block_mode" },
                filter: "enabled = true");

            // Add check constraints for actor system (exclusive arc pattern)
            migrationBuilder.AddCheckConstraint(
                name: "CK_blocklist_subscriptions_exclusive_actor",
                table: "blocklist_subscriptions",
                sql: "(web_user_id IS NOT NULL)::int + (telegram_user_id IS NOT NULL)::int + (system_identifier IS NOT NULL)::int = 1");

            migrationBuilder.AddCheckConstraint(
                name: "CK_domain_filters_exclusive_actor",
                table: "domain_filters",
                sql: "(web_user_id IS NOT NULL)::int + (telegram_user_id IS NOT NULL)::int + (system_identifier IS NOT NULL)::int = 1");

            // Seed 7 Block List Project subscriptions (global scope)
            // 3 Hard blocks (enabled by default): Malware, Phishing, Ransomware
            // 4 Soft blocks (disabled by default): Abuse, Fraud, Redirect, Scam
            migrationBuilder.Sql(@"
                INSERT INTO blocklist_subscriptions
                    (chat_id, name, url, format, block_mode, is_built_in, enabled, refresh_interval_hours, system_identifier, added_date)
                VALUES
                    -- Hard blocks (enabled)
                    (NULL, 'Block List Project - Malware', 'https://blocklistproject.github.io/Lists/alt-version/malware-nl.txt', 0, 2, true, true, 24, 'system_seed', NOW()),
                    (NULL, 'Block List Project - Phishing', 'https://blocklistproject.github.io/Lists/alt-version/phishing-nl.txt', 0, 2, true, true, 24, 'system_seed', NOW()),
                    (NULL, 'Block List Project - Ransomware', 'https://blocklistproject.github.io/Lists/alt-version/ransomware-nl.txt', 0, 2, true, true, 24, 'system_seed', NOW()),

                    -- Soft blocks (disabled - opt-in)
                    (NULL, 'Block List Project - Abuse', 'https://blocklistproject.github.io/Lists/alt-version/abuse-nl.txt', 0, 1, true, false, 24, 'system_seed', NOW()),
                    (NULL, 'Block List Project - Fraud', 'https://blocklistproject.github.io/Lists/alt-version/fraud-nl.txt', 0, 1, true, false, 24, 'system_seed', NOW()),
                    (NULL, 'Block List Project - Redirect', 'https://blocklistproject.github.io/Lists/alt-version/redirect-nl.txt', 0, 1, true, false, 24, 'system_seed', NOW()),
                    (NULL, 'Block List Project - Scam', 'https://blocklistproject.github.io/Lists/alt-version/scam-nl.txt', 0, 1, true, false, 24, 'system_seed', NOW())
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop check constraints first
            migrationBuilder.DropCheckConstraint(
                name: "CK_blocklist_subscriptions_exclusive_actor",
                table: "blocklist_subscriptions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_domain_filters_exclusive_actor",
                table: "domain_filters");

            // Drop tables (seed data will be deleted automatically with tables)
            migrationBuilder.DropTable(
                name: "blocklist_subscriptions");

            migrationBuilder.DropTable(
                name: "cached_blocked_domains");

            migrationBuilder.DropTable(
                name: "domain_filters");
        }
    }
}
