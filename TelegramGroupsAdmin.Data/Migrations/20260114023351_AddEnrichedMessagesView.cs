using Microsoft.EntityFrameworkCore.Migrations;
using TelegramGroupsAdmin.Data.Models;

#nullable disable

namespace TelegramGroupsAdmin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEnrichedMessagesView : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Inline SQL snapshot — the C# constant was later updated for composite keys
            // (chat_id joins) which don't exist at this migration's point in time.
            // AddCompositeMessageKey migration recreates this view with the updated SQL.
            migrationBuilder.Sql("""
                CREATE VIEW enriched_messages AS
                SELECT
                    m.message_id, m.user_id, m.chat_id, m.timestamp, m.message_text,
                    m.photo_file_id, m.photo_file_size, m.urls, m.edit_date, m.content_hash,
                    m.photo_local_path, m.photo_thumbnail_path, m.deleted_at, m.deletion_source,
                    m.reply_to_message_id, m.media_type, m.media_file_id, m.media_file_size,
                    m.media_file_name, m.media_mime_type, m.media_local_path, m.media_duration,
                    m.content_check_skip_reason, m.similarity_hash,
                    c.chat_name, c.chat_icon_path,
                    u.username AS user_name, u.first_name, u.last_name, u.user_photo_path,
                    parent_user.first_name AS reply_to_first_name,
                    parent_user.last_name AS reply_to_last_name,
                    parent_user.username AS reply_to_username,
                    parent_user.telegram_user_id AS reply_to_user_id,
                    parent.message_text AS reply_to_text,
                    t.id AS translation_id, t.translated_text, t.detected_language,
                    t.confidence AS translation_confidence, t.translated_at
                FROM messages m
                LEFT JOIN managed_chats c ON m.chat_id = c.chat_id
                LEFT JOIN telegram_users u ON m.user_id = u.telegram_user_id
                LEFT JOIN messages parent ON m.reply_to_message_id = parent.message_id
                LEFT JOIN telegram_users parent_user ON parent.user_id = parent_user.telegram_user_id
                LEFT JOIN message_translations t ON m.message_id = t.message_id AND t.edit_id IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EnrichedMessageView.DropViewSql);
        }
    }
}
