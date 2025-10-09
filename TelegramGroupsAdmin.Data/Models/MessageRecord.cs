namespace TelegramGroupsAdmin.Data.Models;

// DTO for MessageRecord (PostgreSQL stores booleans as bool)
//
// CRITICAL: All DTO properties MUST use snake_case to match PostgreSQL column names exactly.
// Dapper uses init-only property setters for materialization.
//
// IN SQL QUERIES:
// ✅ CORRECT:   SELECT message_id, user_id FROM messages
// ❌ INCORRECT: SELECT message_id AS MessageId, user_id AS UserId FROM messages
//
// Never use PascalCase aliases (AS SomeColumn) - use raw snake_case column names.
public record MessageRecordDto
{
    public long message_id { get; init; }
    public long user_id { get; init; }
    public string? user_name { get; init; }
    public long chat_id { get; init; }
    public long timestamp { get; init; }
    public string? message_text { get; init; }
    public string? photo_file_id { get; init; }
    public int? photo_file_size { get; init; }
    public string? urls { get; init; }
    public long? edit_date { get; init; }
    public string? content_hash { get; init; }
    public string? chat_name { get; init; }
    public string? photo_local_path { get; init; }
    public string? photo_thumbnail_path { get; init; }

    public MessageRecord ToMessageRecord() => new MessageRecord(
        MessageId: message_id,
        UserId: user_id,
        UserName: user_name,
        ChatId: chat_id,
        Timestamp: timestamp,
        MessageText: message_text,
        PhotoFileId: photo_file_id,
        PhotoFileSize: photo_file_size,
        Urls: urls,
        EditDate: edit_date,
        ContentHash: content_hash,
        ChatName: chat_name,
        PhotoLocalPath: photo_local_path,
        PhotoThumbnailPath: photo_thumbnail_path
    );
}

public record MessageRecord(
    long MessageId,
    long UserId,
    string? UserName,
    long ChatId,
    long Timestamp,
    string? MessageText,
    string? PhotoFileId,
    int? PhotoFileSize,
    string? Urls,
    long? EditDate,
    string? ContentHash,
    string? ChatName,
    string? PhotoLocalPath,
    string? PhotoThumbnailPath
);

// DTO for PhotoMessageRecord (uses actual database column names)
public record PhotoMessageRecordDto
{
    public string photo_file_id { get; init; } = string.Empty;
    public string? message_text { get; init; }
    public long timestamp { get; init; }

    public PhotoMessageRecord ToPhotoMessageRecord() => new PhotoMessageRecord(
        FileId: photo_file_id,
        MessageText: message_text,
        Timestamp: timestamp
    );
}

public record PhotoMessageRecord(
    string FileId,
    string? MessageText,
    long Timestamp
);

// DTO for HistoryStats
public record HistoryStatsDto
{
    public long total_messages { get; init; }
    public long unique_users { get; init; }
    public long photo_count { get; init; }
    public long? oldest_timestamp { get; init; }
    public long? newest_timestamp { get; init; }

    public HistoryStats ToHistoryStats() => new HistoryStats(
        TotalMessages: total_messages,
        UniqueUsers: unique_users,
        PhotoCount: photo_count,
        OldestTimestamp: oldest_timestamp,
        NewestTimestamp: newest_timestamp
    );
}

public record HistoryStats(
    long TotalMessages,
    long UniqueUsers,
    long PhotoCount,
    long? OldestTimestamp,
    long? NewestTimestamp
);

// DTO for MessageEditRecord
public record MessageEditRecordDto
{
    public long id { get; init; }
    public long message_id { get; init; }
    public long edit_date { get; init; }
    public string? old_text { get; init; }
    public string? new_text { get; init; }
    public string? old_content_hash { get; init; }
    public string? new_content_hash { get; init; }

    public MessageEditRecord ToMessageEditRecord() => new MessageEditRecord(
        Id: id,
        MessageId: message_id,
        EditDate: edit_date,
        OldText: old_text,
        NewText: new_text,
        OldContentHash: old_content_hash,
        NewContentHash: new_content_hash
    );
}

public record MessageEditRecord(
    long Id,
    long MessageId,
    long EditDate,
    string? OldText,
    string? NewText,
    string? OldContentHash,
    string? NewContentHash
);

// DTO for SpamCheckRecord (has boolean field is_spam)
public record SpamCheckRecordDto
{
    public long id { get; init; }
    public long check_timestamp { get; init; }
    public long user_id { get; init; }
    public string? content_hash { get; init; }
    public bool is_spam { get; init; }
    public int confidence { get; init; }
    public string? reason { get; init; }
    public string check_type { get; init; } = string.Empty;
    public long? matched_message_id { get; init; }

    public SpamCheckRecord ToSpamCheckRecord() => new SpamCheckRecord(
        Id: id,
        CheckTimestamp: check_timestamp,
        UserId: user_id,
        ContentHash: content_hash,
        IsSpam: is_spam,
        Confidence: confidence,
        Reason: reason,
        CheckType: check_type,
        MatchedMessageId: matched_message_id
    );
}

public record SpamCheckRecord(
    long Id,
    long CheckTimestamp,
    long UserId,
    string? ContentHash,
    bool IsSpam,
    int Confidence,
    string? Reason,
    string CheckType,
    long? MatchedMessageId
);
