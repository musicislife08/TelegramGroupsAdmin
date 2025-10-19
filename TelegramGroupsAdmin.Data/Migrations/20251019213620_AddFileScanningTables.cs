using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TgSpam_PreFilterApi.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFileScanningTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "file_scan_quota",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    service = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    quota_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    quota_window_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    quota_window_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    count = table.Column<int>(type: "integer", nullable: false),
                    limit_value = table.Column<int>(type: "integer", nullable: false),
                    last_updated = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_file_scan_quota", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "file_scan_results",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    file_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    scanner = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    result = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    threat_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    scan_duration_ms = table.Column<int>(type: "integer", nullable: true),
                    scanned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    metadata = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_file_scan_results", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_file_scan_quota_service_quota_type_quota_window_end",
                table: "file_scan_quota",
                columns: new[] { "service", "quota_type", "quota_window_end" });

            migrationBuilder.CreateIndex(
                name: "IX_file_scan_quota_service_quota_type_quota_window_start",
                table: "file_scan_quota",
                columns: new[] { "service", "quota_type", "quota_window_start" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_file_scan_results_file_hash",
                table: "file_scan_results",
                column: "file_hash");

            migrationBuilder.CreateIndex(
                name: "IX_file_scan_results_scanner_scanned_at",
                table: "file_scan_results",
                columns: new[] { "scanner", "scanned_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "file_scan_quota");

            migrationBuilder.DropTable(
                name: "file_scan_results");
        }
    }
}
