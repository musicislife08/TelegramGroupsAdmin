using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TgSpam_PreFilterApi.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIndexOnWelcomeResponseTimeoutJobId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_welcome_responses_timeout_job_id",
                table: "welcome_responses",
                column: "timeout_job_id",
                filter: "timeout_job_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_welcome_responses_timeout_job_id",
                table: "welcome_responses");
        }
    }
}
