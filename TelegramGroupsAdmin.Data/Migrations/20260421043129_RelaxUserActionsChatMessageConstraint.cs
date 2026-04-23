using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class RelaxUserActionsChatMessageConstraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_user_actions_message_chat_null_consistency",
                table: "user_actions");

            migrationBuilder.AddCheckConstraint(
                name: "CK_user_actions_message_chat_null_consistency",
                table: "user_actions",
                sql: "(message_id IS NULL) OR (chat_id IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_user_actions_message_chat_null_consistency",
                table: "user_actions");

            migrationBuilder.AddCheckConstraint(
                name: "CK_user_actions_message_chat_null_consistency",
                table: "user_actions",
                sql: "(message_id IS NULL) = (chat_id IS NULL)");
        }
    }
}
