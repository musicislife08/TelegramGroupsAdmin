namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// Data model for chat_admins table (database DTO)
/// Tracks Telegram admin status per chat for permission caching
/// </summary>
public record ChatAdminRecord
{
    public long id { get; init; }
    public long chat_id { get; init; }
    public long telegram_id { get; init; }
    public bool is_creator { get; init; }
    public long promoted_at { get; init; }
    public long last_verified_at { get; init; }
    public bool is_active { get; init; }
}
