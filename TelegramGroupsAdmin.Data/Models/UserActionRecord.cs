namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// User action type enum (Data layer - stored as INT in database)
/// </summary>
public enum UserActionType
{
    Ban = 0,
    Warn = 1,
    Mute = 2,
    Trust = 3,
    Unban = 4
}

// DTO for UserActionRecord (PostgreSQL user_actions table)
//
// CRITICAL: All DTO properties MUST use snake_case to match PostgreSQL column names exactly.
// Dapper uses init-only property setters for materialization.
// All actions are global - origin chat can be tracked via message_id FK
public record UserActionRecordDto
{
    public long id { get; init; }
    public long user_id { get; init; }
    public int action_type { get; init; }
    public long? message_id { get; init; }
    public string? issued_by { get; init; }
    public long issued_at { get; init; }
    public long? expires_at { get; init; }
    public string? reason { get; init; }

    public UserActionRecord ToUserActionRecord() => new UserActionRecord(
        Id: id,
        UserId: user_id,
        ActionType: action_type,
        MessageId: message_id,
        IssuedBy: issued_by,
        IssuedAt: issued_at,
        ExpiresAt: expires_at,
        Reason: reason
    );
}

public record UserActionRecord(
    long Id,
    long UserId,
    int ActionType,
    long? MessageId,
    string? IssuedBy,
    long IssuedAt,
    long? ExpiresAt,
    string? Reason
);
