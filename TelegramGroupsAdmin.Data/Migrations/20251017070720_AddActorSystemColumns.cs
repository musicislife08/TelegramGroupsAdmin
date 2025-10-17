using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TgSpam_PreFilterApi.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddActorSystemColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "actor_system_identifier",
                table: "user_tags",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "actor_telegram_user_id",
                table: "user_tags",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "actor_web_user_id",
                table: "user_tags",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "system_identifier",
                table: "user_actions",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "telegram_user_id",
                table: "user_actions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "web_user_id",
                table: "user_actions",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "system_identifier",
                table: "stop_words",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "telegram_user_id",
                table: "stop_words",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "web_user_id",
                table: "stop_words",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "system_identifier",
                table: "detection_results",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "telegram_user_id",
                table: "detection_results",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "web_user_id",
                table: "detection_results",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "actor_system_identifier",
                table: "admin_notes",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "actor_telegram_user_id",
                table: "admin_notes",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "actor_web_user_id",
                table: "admin_notes",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_tags_actor_telegram_user_id",
                table: "user_tags",
                column: "actor_telegram_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_user_tags_actor_web_user_id",
                table: "user_tags",
                column: "actor_web_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_user_actions_telegram_user_id",
                table: "user_actions",
                column: "telegram_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_user_actions_web_user_id",
                table: "user_actions",
                column: "web_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_stop_words_telegram_user_id",
                table: "stop_words",
                column: "telegram_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_stop_words_web_user_id",
                table: "stop_words",
                column: "web_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_detection_results_telegram_user_id",
                table: "detection_results",
                column: "telegram_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_detection_results_web_user_id",
                table: "detection_results",
                column: "web_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_admin_notes_actor_telegram_user_id",
                table: "admin_notes",
                column: "actor_telegram_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_admin_notes_actor_web_user_id",
                table: "admin_notes",
                column: "actor_web_user_id");

            migrationBuilder.AddForeignKey(
                name: "FK_admin_notes_telegram_users_actor_telegram_user_id",
                table: "admin_notes",
                column: "actor_telegram_user_id",
                principalTable: "telegram_users",
                principalColumn: "telegram_user_id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_admin_notes_users_actor_web_user_id",
                table: "admin_notes",
                column: "actor_web_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_detection_results_telegram_users_telegram_user_id",
                table: "detection_results",
                column: "telegram_user_id",
                principalTable: "telegram_users",
                principalColumn: "telegram_user_id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_detection_results_users_web_user_id",
                table: "detection_results",
                column: "web_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_stop_words_telegram_users_telegram_user_id",
                table: "stop_words",
                column: "telegram_user_id",
                principalTable: "telegram_users",
                principalColumn: "telegram_user_id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_stop_words_users_web_user_id",
                table: "stop_words",
                column: "web_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_user_actions_telegram_users_telegram_user_id",
                table: "user_actions",
                column: "telegram_user_id",
                principalTable: "telegram_users",
                principalColumn: "telegram_user_id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_user_actions_users_web_user_id",
                table: "user_actions",
                column: "web_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_user_tags_telegram_users_actor_telegram_user_id",
                table: "user_tags",
                column: "actor_telegram_user_id",
                principalTable: "telegram_users",
                principalColumn: "telegram_user_id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_user_tags_users_actor_web_user_id",
                table: "user_tags",
                column: "actor_web_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_admin_notes_telegram_users_actor_telegram_user_id",
                table: "admin_notes");

            migrationBuilder.DropForeignKey(
                name: "FK_admin_notes_users_actor_web_user_id",
                table: "admin_notes");

            migrationBuilder.DropForeignKey(
                name: "FK_detection_results_telegram_users_telegram_user_id",
                table: "detection_results");

            migrationBuilder.DropForeignKey(
                name: "FK_detection_results_users_web_user_id",
                table: "detection_results");

            migrationBuilder.DropForeignKey(
                name: "FK_stop_words_telegram_users_telegram_user_id",
                table: "stop_words");

            migrationBuilder.DropForeignKey(
                name: "FK_stop_words_users_web_user_id",
                table: "stop_words");

            migrationBuilder.DropForeignKey(
                name: "FK_user_actions_telegram_users_telegram_user_id",
                table: "user_actions");

            migrationBuilder.DropForeignKey(
                name: "FK_user_actions_users_web_user_id",
                table: "user_actions");

            migrationBuilder.DropForeignKey(
                name: "FK_user_tags_telegram_users_actor_telegram_user_id",
                table: "user_tags");

            migrationBuilder.DropForeignKey(
                name: "FK_user_tags_users_actor_web_user_id",
                table: "user_tags");

            migrationBuilder.DropIndex(
                name: "IX_user_tags_actor_telegram_user_id",
                table: "user_tags");

            migrationBuilder.DropIndex(
                name: "IX_user_tags_actor_web_user_id",
                table: "user_tags");

            migrationBuilder.DropIndex(
                name: "IX_user_actions_telegram_user_id",
                table: "user_actions");

            migrationBuilder.DropIndex(
                name: "IX_user_actions_web_user_id",
                table: "user_actions");

            migrationBuilder.DropIndex(
                name: "IX_stop_words_telegram_user_id",
                table: "stop_words");

            migrationBuilder.DropIndex(
                name: "IX_stop_words_web_user_id",
                table: "stop_words");

            migrationBuilder.DropIndex(
                name: "IX_detection_results_telegram_user_id",
                table: "detection_results");

            migrationBuilder.DropIndex(
                name: "IX_detection_results_web_user_id",
                table: "detection_results");

            migrationBuilder.DropIndex(
                name: "IX_admin_notes_actor_telegram_user_id",
                table: "admin_notes");

            migrationBuilder.DropIndex(
                name: "IX_admin_notes_actor_web_user_id",
                table: "admin_notes");

            migrationBuilder.DropColumn(
                name: "actor_system_identifier",
                table: "user_tags");

            migrationBuilder.DropColumn(
                name: "actor_telegram_user_id",
                table: "user_tags");

            migrationBuilder.DropColumn(
                name: "actor_web_user_id",
                table: "user_tags");

            migrationBuilder.DropColumn(
                name: "system_identifier",
                table: "user_actions");

            migrationBuilder.DropColumn(
                name: "telegram_user_id",
                table: "user_actions");

            migrationBuilder.DropColumn(
                name: "web_user_id",
                table: "user_actions");

            migrationBuilder.DropColumn(
                name: "system_identifier",
                table: "stop_words");

            migrationBuilder.DropColumn(
                name: "telegram_user_id",
                table: "stop_words");

            migrationBuilder.DropColumn(
                name: "web_user_id",
                table: "stop_words");

            migrationBuilder.DropColumn(
                name: "system_identifier",
                table: "detection_results");

            migrationBuilder.DropColumn(
                name: "telegram_user_id",
                table: "detection_results");

            migrationBuilder.DropColumn(
                name: "web_user_id",
                table: "detection_results");

            migrationBuilder.DropColumn(
                name: "actor_system_identifier",
                table: "admin_notes");

            migrationBuilder.DropColumn(
                name: "actor_telegram_user_id",
                table: "admin_notes");

            migrationBuilder.DropColumn(
                name: "actor_web_user_id",
                table: "admin_notes");
        }
    }
}
