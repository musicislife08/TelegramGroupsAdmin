namespace TelegramGroupsAdmin.Data.Models;

// DTO for MessageRecord (SQLite stores booleans as Int64)
//
// CRITICAL: All DTO properties MUST use snake_case to match SQLite column names exactly.
// Dapper uses positional constructor matching and is CASE-SENSITIVE.
//
// IN SQL QUERIES:
// ✅ CORRECT:   SELECT message_id, user_id FROM messages
// ❌ INCORRECT: SELECT message_id AS MessageId, user_id AS UserId FROM messages
//
// Never use PascalCase aliases (AS SomeColumn) - use raw snake_case column names.
internal record MessageRecordDto(
    long message_id,
    long user_id,
    string? user_name,
    long chat_id,
    long timestamp,
    long expires_at,
    string? message_text,
    string? photo_file_id,
    long? photo_file_size,
    string? urls,
    long? edit_date,
    string? content_hash,
    string? chat_name,
    string? photo_local_path,
    string? photo_thumbnail_path
)
{
    public MessageRecord ToMessageRecord() => new MessageRecord(
        MessageId: message_id,
        UserId: user_id,
        UserName: user_name,
        ChatId: chat_id,
        Timestamp: timestamp,
        ExpiresAt: expires_at,
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
    long ExpiresAt,
    string? MessageText,
    string? PhotoFileId,
    long? PhotoFileSize,
    string? Urls,
    long? EditDate,
    string? ContentHash,
    string? ChatName,
    string? PhotoLocalPath,
    string? PhotoThumbnailPath
);

// DTO for PhotoMessageRecord
internal record PhotoMessageRecordDto(
    string file_id,
    string? message_text,
    long timestamp
)
{
    public PhotoMessageRecord ToPhotoMessageRecord() => new PhotoMessageRecord(
        FileId: file_id,
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
internal record HistoryStatsDto(
    long total_messages,
    long unique_users,
    long photo_count,
    long? oldest_timestamp,
    long? newest_timestamp
)
{
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
internal record MessageEditRecordDto(
    long id,
    long message_id,
    long edit_date,
    string? old_text,
    string? new_text,
    string? old_content_hash,
    string? new_content_hash
)
{
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
internal record SpamCheckRecordDto(
    long id,
    long check_timestamp,
    long user_id,
    string? content_hash,
    long is_spam,
    long confidence,
    string? reason,
    string check_type,
    long? matched_message_id
)
{
    public SpamCheckRecord ToSpamCheckRecord() => new SpamCheckRecord(
        Id: id,
        CheckTimestamp: check_timestamp,
        UserId: user_id,
        ContentHash: content_hash,
        IsSpam: is_spam != 0,
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
    long Confidence,
    string? Reason,
    string CheckType,
    long? MatchedMessageId
);
