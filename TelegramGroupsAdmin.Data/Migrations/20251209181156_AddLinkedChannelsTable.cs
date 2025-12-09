using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLinkedChannelsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "linked_channels",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    managed_chat_id = table.Column<long>(type: "bigint", nullable: false),
                    channel_id = table.Column<long>(type: "bigint", nullable: false),
                    channel_name = table.Column<string>(type: "text", nullable: true),
                    channel_icon_path = table.Column<string>(type: "text", nullable: true),
                    photo_hash = table.Column<byte[]>(type: "bytea", nullable: true),
                    last_synced = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_linked_channels", x => x.id);
                    table.ForeignKey(
                        name: "FK_linked_channels_managed_chats_managed_chat_id",
                        column: x => x.managed_chat_id,
                        principalTable: "managed_chats",
                        principalColumn: "chat_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_linked_channels_channel_id",
                table: "linked_channels",
                column: "channel_id");

            migrationBuilder.CreateIndex(
                name: "IX_linked_channels_managed_chat_id",
                table: "linked_channels",
                column: "managed_chat_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "linked_channels");
        }
    }
}
