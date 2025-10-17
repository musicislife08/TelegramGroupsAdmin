using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TgSpam_PreFilterApi.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddImpersonationAlertsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "impersonation_alerts",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    suspected_user_id = table.Column<long>(type: "bigint", nullable: false),
                    target_user_id = table.Column<long>(type: "bigint", nullable: false),
                    chat_id = table.Column<long>(type: "bigint", nullable: false),
                    total_score = table.Column<int>(type: "integer", nullable: false),
                    risk_level = table.Column<int>(type: "integer", nullable: false),
                    name_match = table.Column<bool>(type: "boolean", nullable: false),
                    photo_match = table.Column<bool>(type: "boolean", nullable: false),
                    photo_similarity_score = table.Column<double>(type: "double precision", nullable: true),
                    detected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    auto_banned = table.Column<bool>(type: "boolean", nullable: false),
                    reviewed_by_user_id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    verdict = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_impersonation_alerts", x => x.id);
                    table.ForeignKey(
                        name: "FK_impersonation_alerts_telegram_users_suspected_user_id",
                        column: x => x.suspected_user_id,
                        principalTable: "telegram_users",
                        principalColumn: "telegram_user_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_impersonation_alerts_telegram_users_target_user_id",
                        column: x => x.target_user_id,
                        principalTable: "telegram_users",
                        principalColumn: "telegram_user_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_impersonation_alerts_users_reviewed_by_user_id",
                        column: x => x.reviewed_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_impersonation_alerts_chat_id",
                table: "impersonation_alerts",
                column: "chat_id");

            migrationBuilder.CreateIndex(
                name: "IX_impersonation_alerts_reviewed_by_user_id",
                table: "impersonation_alerts",
                column: "reviewed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_impersonation_alerts_risk_level_detected_at",
                table: "impersonation_alerts",
                columns: new[] { "risk_level", "detected_at" },
                filter: "reviewed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_impersonation_alerts_suspected_user_id",
                table: "impersonation_alerts",
                column: "suspected_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_impersonation_alerts_target_user_id",
                table: "impersonation_alerts",
                column: "target_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "impersonation_alerts");
        }
    }
}
