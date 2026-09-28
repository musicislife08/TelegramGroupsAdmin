using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceReviewDismissWithReviewClean : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_detection_results_source_classification",
                table: "detection_results");

            migrationBuilder.AddCheckConstraint(
                name: "CK_detection_results_source_classification",
                table: "detection_results",
                sql: "(source IN (10, 11, 13, 14) AND classification = 0)\nOR (source IN (12, 19) AND classification = 1)\nOR (source IN (16, 18, 99) AND classification IN (0, 1))\nOR (source IN (1, 17) AND classification IN (4, 5))\nOR (source = 0 AND classification IN (2, 3, 4, 5))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_detection_results_source_classification",
                table: "detection_results");

            // ReviewClean (19) has no pre-migration equivalent; WebMarkHam (12) keeps it ExplicitHam.
            migrationBuilder.Sql("UPDATE detection_results SET source = 12 WHERE source = 19;");

            migrationBuilder.AddCheckConstraint(
                name: "CK_detection_results_source_classification",
                table: "detection_results",
                sql: "(source IN (10, 11, 13, 14) AND classification = 0)\nOR (source = 12 AND classification = 1)\nOR (source = 15 AND classification = 3)\nOR (source IN (16, 18, 99) AND classification IN (0, 1))\nOR (source IN (1, 17) AND classification IN (4, 5))\nOR (source = 0 AND classification IN (2, 3, 4, 5))");
        }
    }
}
