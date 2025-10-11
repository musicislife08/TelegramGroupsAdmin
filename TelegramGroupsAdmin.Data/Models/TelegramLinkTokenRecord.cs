namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// DTO for telegram_link_tokens table (database DTO)
/// One-time tokens for linking Telegram accounts to web users
/// </summary>
public record TelegramLinkTokenRecordDto
{
    public string token { get; init; } = string.Empty;
    public string user_id { get; init; } = string.Empty;
    public long created_at { get; init; }
    public long expires_at { get; init; }
    public long? used_at { get; init; }
    public long? used_by_telegram_id { get; init; }
}

public record TelegramLinkTokenRecord(
    string Token,
    string UserId,
    long CreatedAt,
    long ExpiresAt,
    long? UsedAt,
    long? UsedByTelegramId
);
