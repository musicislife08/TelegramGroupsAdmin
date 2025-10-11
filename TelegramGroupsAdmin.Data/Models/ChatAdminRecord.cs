namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// DTO for chat_admins table (database DTO)
/// Tracks Telegram admin status per chat for permission caching
/// </summary>
public record ChatAdminRecordDto
{
    public long id { get; init; }
    public long chat_id { get; init; }
    public long telegram_id { get; init; }
    public bool is_creator { get; init; }
    public long promoted_at { get; init; }
    public long last_verified_at { get; init; }
    public bool is_active { get; init; }
}

public record ChatAdminRecord(
    long Id,
    long ChatId,
    long TelegramId,
    bool IsCreator,
    long PromotedAt,
    long LastVerifiedAt,
    bool IsActive
);
