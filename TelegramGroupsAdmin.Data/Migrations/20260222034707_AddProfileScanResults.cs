using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProfileScanResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "profile_scan_results",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    scanned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    score = table.Column<decimal>(type: "numeric(3,1)", precision: 3, scale: 1, nullable: false),
                    outcome = table.Column<int>(type: "integer", nullable: false),
                    rule_score = table.Column<decimal>(type: "numeric(3,1)", precision: 3, scale: 1, nullable: false),
                    ai_score = table.Column<decimal>(type: "numeric(3,1)", precision: 3, scale: 1, nullable: false),
                    ai_confidence = table.Column<int>(type: "integer", nullable: true),
                    ai_reason = table.Column<string>(type: "text", nullable: true),
                    ai_signals = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_profile_scan_results", x => x.id);
                    table.ForeignKey(
                        name: "FK_profile_scan_results_telegram_users_user_id",
                        column: x => x.user_id,
                        principalTable: "telegram_users",
                        principalColumn: "telegram_user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_profile_scan_results_user_id_scanned_at",
                table: "profile_scan_results",
                columns: new[] { "user_id", "scanned_at" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "profile_scan_results");
        }
    }
}
