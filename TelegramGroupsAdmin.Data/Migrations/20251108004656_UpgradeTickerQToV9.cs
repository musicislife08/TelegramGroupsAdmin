using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class UpgradeTickerQToV9 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TimeTickers_TimeTickers_BatchParent",
                schema: "ticker",
                table: "TimeTickers");

            migrationBuilder.DropIndex(
                name: "IX_TimeTicker_Status_ExecutionTime",
                schema: "ticker",
                table: "TimeTickers");

            migrationBuilder.RenameColumn(
                name: "Exception",
                schema: "ticker",
                table: "TimeTickers",
                newName: "SkippedReason");

            migrationBuilder.RenameColumn(
                name: "BatchRunCondition",
                schema: "ticker",
                table: "TimeTickers",
                newName: "RunCondition");

            migrationBuilder.RenameColumn(
                name: "BatchParent",
                schema: "ticker",
                table: "TimeTickers",
                newName: "ParentId");

            migrationBuilder.RenameIndex(
                name: "IX_TimeTickers_BatchParent",
                schema: "ticker",
                table: "TimeTickers",
                newName: "IX_TimeTickers_ParentId");

            migrationBuilder.RenameColumn(
                name: "Exception",
                schema: "ticker",
                table: "CronTickerOccurrences",
                newName: "SkippedReason");

            migrationBuilder.AlterColumn<DateTime>(
                name: "ExecutionTime",
                schema: "ticker",
                table: "TimeTickers",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AddColumn<string>(
                name: "ExceptionMessage",
                schema: "ticker",
                table: "TimeTickers",
                type: "text",
                nullable: true);

            // Add new columns with temporary defaults
            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                schema: "ticker",
                table: "CronTickerOccurrences",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "(NOW() AT TIME ZONE 'UTC')");

            migrationBuilder.AddColumn<string>(
                name: "ExceptionMessage",
                schema: "ticker",
                table: "CronTickerOccurrences",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                schema: "ticker",
                table: "CronTickerOccurrences",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "(NOW() AT TIME ZONE 'UTC')");

            // Backfill CreatedAt/UpdatedAt for existing rows using ExecutionTime as best estimate
            // (All existing rows get backfilled; new rows inserted after migration will use DB defaults)
            migrationBuilder.Sql(@"
                UPDATE ticker.""CronTickerOccurrences""
                SET ""CreatedAt"" = ""ExecutionTime"",
                    ""UpdatedAt"" = COALESCE(""ExecutedAt"", ""ExecutionTime"")
            ");

            migrationBuilder.CreateIndex(
                name: "IX_TimeTicker_Status_ExecutionTime",
                schema: "ticker",
                table: "TimeTickers",
                columns: new[] { "Status", "ExecutionTime", "Request" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Function_Expression_Request",
                schema: "ticker",
                table: "CronTickers",
                columns: new[] { "Function", "Expression", "Request" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_TimeTickers_TimeTickers_ParentId",
                schema: "ticker",
                table: "TimeTickers",
                column: "ParentId",
                principalSchema: "ticker",
                principalTable: "TimeTickers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TimeTickers_TimeTickers_ParentId",
                schema: "ticker",
                table: "TimeTickers");

            migrationBuilder.DropIndex(
                name: "IX_TimeTicker_Status_ExecutionTime",
                schema: "ticker",
                table: "TimeTickers");

            migrationBuilder.DropIndex(
                name: "IX_Function_Expression_Request",
                schema: "ticker",
                table: "CronTickers");

            migrationBuilder.DropColumn(
                name: "ExceptionMessage",
                schema: "ticker",
                table: "TimeTickers");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                schema: "ticker",
                table: "CronTickerOccurrences");

            migrationBuilder.DropColumn(
                name: "ExceptionMessage",
                schema: "ticker",
                table: "CronTickerOccurrences");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                schema: "ticker",
                table: "CronTickerOccurrences");

            migrationBuilder.RenameColumn(
                name: "SkippedReason",
                schema: "ticker",
                table: "TimeTickers",
                newName: "Exception");

            migrationBuilder.RenameColumn(
                name: "RunCondition",
                schema: "ticker",
                table: "TimeTickers",
                newName: "BatchRunCondition");

            migrationBuilder.RenameColumn(
                name: "ParentId",
                schema: "ticker",
                table: "TimeTickers",
                newName: "BatchParent");

            migrationBuilder.RenameIndex(
                name: "IX_TimeTickers_ParentId",
                schema: "ticker",
                table: "TimeTickers",
                newName: "IX_TimeTickers_BatchParent");

            migrationBuilder.RenameColumn(
                name: "SkippedReason",
                schema: "ticker",
                table: "CronTickerOccurrences",
                newName: "Exception");

            migrationBuilder.AlterColumn<DateTime>(
                name: "ExecutionTime",
                schema: "ticker",
                table: "TimeTickers",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TimeTicker_Status_ExecutionTime",
                schema: "ticker",
                table: "TimeTickers",
                columns: new[] { "Status", "ExecutionTime" });

            migrationBuilder.AddForeignKey(
                name: "FK_TimeTickers_TimeTickers_BatchParent",
                schema: "ticker",
                table: "TimeTickers",
                column: "BatchParent",
                principalSchema: "ticker",
                principalTable: "TimeTickers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
