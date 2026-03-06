using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class CleanupV1Remnants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "threshold_recommendations");

            // Migrate HashMatchConfidence from V1 (0-100 int) to V2 (0.0-5.0 double) in JSONB config
            // Only convert values > 5.0 (already on V2 scale if <= 5.0)
            migrationBuilder.Sql("""
                UPDATE content_detection_configs
                SET config_json = jsonb_set(config_json, '{ImageContent,HashMatchConfidence}',
                    to_jsonb((config_json #>> '{ImageContent,HashMatchConfidence}')::double precision / 20.0))
                WHERE config_json #>> '{ImageContent,HashMatchConfidence}' IS NOT NULL
                  AND (config_json #>> '{ImageContent,HashMatchConfidence}')::double precision > 5.0;

                UPDATE content_detection_configs
                SET config_json = jsonb_set(config_json, '{VideoContent,HashMatchConfidence}',
                    to_jsonb((config_json #>> '{VideoContent,HashMatchConfidence}')::double precision / 20.0))
                WHERE config_json #>> '{VideoContent,HashMatchConfidence}' IS NOT NULL
                  AND (config_json #>> '{VideoContent,HashMatchConfidence}')::double precision > 5.0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "threshold_recommendations",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    reviewed_by_user_id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    algorithm_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    confidence_score = table.Column<decimal>(type: "numeric", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    current_threshold = table.Column<decimal>(type: "numeric", nullable: true),
                    estimated_veto_rate_after = table.Column<decimal>(type: "numeric", nullable: true),
                    recommended_threshold = table.Column<decimal>(type: "numeric", nullable: false),
                    review_notes = table.Column<string>(type: "text", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    sample_vetoed_message_ids = table.Column<long[]>(type: "bigint[]", nullable: true),
                    spam_flags_count = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    training_period_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    training_period_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    veto_rate_before = table.Column<decimal>(type: "numeric", nullable: false),
                    vetoed_count = table.Column<int>(type: "integer", nullable: false)
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
    }
}
