using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class ConvertTimeoutJobIdToString : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "timeout_job_id",
                table: "welcome_responses",
                type: "text",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // PostgreSQL requires explicit USING clause to cast text to uuid
            migrationBuilder.Sql(@"
                ALTER TABLE welcome_responses
                ALTER COLUMN timeout_job_id TYPE uuid
                USING timeout_job_id::uuid;
            ");
        }
    }
}
