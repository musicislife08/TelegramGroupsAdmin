using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class ClearV1PhotoHashes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<byte[]>(
                name: "photo_hash",
                table: "image_training_samples",
                type: "bytea",
                nullable: true,
                oldClrType: typeof(byte[]),
                oldType: "bytea");

            // Every stored hash was produced by ImageSharp's resampler and cannot be
            // compared against a v2 hash. Clearing them leaves detection degraded to
            // misses (never false positives) until PhotoHashRehashService refills
            // whatever is still recoverable from disk.
            migrationBuilder.Sql("UPDATE telegram_users SET photo_hash = NULL WHERE photo_hash IS NOT NULL;");
            migrationBuilder.Sql("UPDATE linked_channels SET photo_hash = NULL WHERE photo_hash IS NOT NULL;");
            migrationBuilder.Sql("UPDATE ban_celebration_gifs SET photo_hash = NULL WHERE photo_hash IS NOT NULL;");
            migrationBuilder.Sql("UPDATE image_training_samples SET photo_hash = NULL WHERE photo_hash IS NOT NULL;");
            migrationBuilder.Sql("UPDATE video_training_samples SET keyframe_hashes = '[]'::jsonb WHERE keyframe_hashes <> '[]'::jsonb;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<byte[]>(
                name: "photo_hash",
                table: "image_training_samples",
                type: "bytea",
                nullable: false,
                defaultValue: new byte[0],
                oldClrType: typeof(byte[]),
                oldType: "bytea",
                oldNullable: true);
        }
    }
}
