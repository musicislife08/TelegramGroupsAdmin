namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// Bot status in a managed chat (Data layer enum - stored as INT in database)
/// </summary>
public enum BotChatStatus
{
    Member = 0,
    Administrator = 1,
    Left = 2,
    Kicked = 3
}

/// <summary>
/// Chat type categories (Data layer enum - stored as INT in database)
/// </summary>
public enum ManagedChatType
{
    Private = 0,
    Group = 1,
    Supergroup = 2,
    Channel = 3
}

// DTO for ManagedChatRecord (PostgreSQL managed_chats table)
//
// CRITICAL: All DTO properties MUST use snake_case to match PostgreSQL column names exactly.
// Dapper uses init-only property setters for materialization.
//
// Note: chat_type and bot_status are stored as INT32 in PostgreSQL
public record ManagedChatRecordDto
{
    public long chat_id { get; init; }
    public string? chat_name { get; init; }
    public int chat_type { get; init; }
    public int bot_status { get; init; }
    public bool is_admin { get; init; }
    public long added_at { get; init; }
    public bool is_active { get; init; }
    public long? last_seen_at { get; init; }
    public string? settings_json { get; init; }

    public ManagedChatRecord ToManagedChatRecord() => new ManagedChatRecord(
        ChatId: chat_id,
        ChatName: chat_name,
        ChatType: chat_type,
        BotStatus: bot_status,
        IsAdmin: is_admin,
        AddedAt: added_at,
        IsActive: is_active,
        LastSeenAt: last_seen_at,
        SettingsJson: settings_json
    );
}

public record ManagedChatRecord(
    long ChatId,
    string? ChatName,
    int ChatType,
    int BotStatus,
    bool IsAdmin,
    long AddedAt,
    bool IsActive,
    long? LastSeenAt,
    string? SettingsJson
);
