namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// Data model for telegram_user_mappings table (database DTO)
/// Maps Telegram users to web app users for permission checking
/// </summary>
public record TelegramUserMappingRecord
{
    public long id { get; init; }
    public long telegram_id { get; init; }
    public string? telegram_username { get; init; }
    public string user_id { get; init; } = string.Empty;
    public long linked_at { get; init; }
    public bool is_active { get; init; }
}
