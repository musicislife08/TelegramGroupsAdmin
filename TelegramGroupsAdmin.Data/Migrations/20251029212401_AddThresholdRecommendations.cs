using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddThresholdRecommendations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "threshold_recommendations",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    algorithm_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    current_threshold = table.Column<decimal>(type: "numeric", nullable: true),
                    recommended_threshold = table.Column<decimal>(type: "numeric", nullable: false),
                    confidence_score = table.Column<decimal>(type: "numeric", nullable: false),
                    veto_rate_before = table.Column<decimal>(type: "numeric", nullable: false),
                    estimated_veto_rate_after = table.Column<decimal>(type: "numeric", nullable: true),
                    sample_vetoed_message_ids = table.Column<long[]>(type: "bigint[]", nullable: true),
                    spam_flags_count = table.Column<int>(type: "integer", nullable: false),
                    vetoed_count = table.Column<int>(type: "integer", nullable: false),
                    training_period_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    training_period_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reviewed_by_user_id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    review_notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_threshold_recommendations", x => x.id);
                    table.ForeignKey(
                        name: "FK_threshold_recommendations_users_reviewed_by_user_id",
                        column: x => x.reviewed_by_user_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_threshold_recommendations_algorithm_name_status",
                table: "threshold_recommendations",
                columns: new[] { "algorithm_name", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_threshold_recommendations_created_at",
                table: "threshold_recommendations",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_threshold_recommendations_reviewed_by_user_id",
                table: "threshold_recommendations",
                column: "reviewed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_threshold_recommendations_status",
                table: "threshold_recommendations",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "threshold_recommendations");
        }
    }
}
