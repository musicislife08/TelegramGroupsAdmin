using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TgSpam_PreFilterApi.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddActorSystemCheckConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_user_tags_exclusive_actor",
                table: "user_tags",
                sql: "(actor_web_user_id IS NOT NULL)::int + (actor_telegram_user_id IS NOT NULL)::int + (actor_system_identifier IS NOT NULL)::int = 1");

            migrationBuilder.AddCheckConstraint(
                name: "CK_user_actions_exclusive_actor",
                table: "user_actions",
                sql: "(web_user_id IS NOT NULL)::int + (telegram_user_id IS NOT NULL)::int + (system_identifier IS NOT NULL)::int = 1");

            migrationBuilder.AddCheckConstraint(
                name: "CK_stop_words_exclusive_actor",
                table: "stop_words",
                sql: "(web_user_id IS NOT NULL)::int + (telegram_user_id IS NOT NULL)::int + (system_identifier IS NOT NULL)::int = 1");

            migrationBuilder.AddCheckConstraint(
                name: "CK_detection_results_exclusive_actor",
                table: "detection_results",
                sql: "(web_user_id IS NOT NULL)::int + (telegram_user_id IS NOT NULL)::int + (system_identifier IS NOT NULL)::int = 1");

            migrationBuilder.AddCheckConstraint(
                name: "CK_admin_notes_exclusive_actor",
                table: "admin_notes",
                sql: "(actor_web_user_id IS NOT NULL)::int + (actor_telegram_user_id IS NOT NULL)::int + (actor_system_identifier IS NOT NULL)::int = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_user_tags_exclusive_actor",
                table: "user_tags");

            migrationBuilder.DropCheckConstraint(
                name: "CK_user_actions_exclusive_actor",
                table: "user_actions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_stop_words_exclusive_actor",
                table: "stop_words");

            migrationBuilder.DropCheckConstraint(
                name: "CK_detection_results_exclusive_actor",
                table: "detection_results");

            migrationBuilder.DropCheckConstraint(
                name: "CK_admin_notes_exclusive_actor",
                table: "admin_notes");
        }
    }
}
