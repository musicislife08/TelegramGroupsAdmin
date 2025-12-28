using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Ui.Models;

/// <summary>
/// Aggregate response for the Messages page initial load.
/// Bundles all data needed to render the page in a single HTTP call.
/// </summary>
public record MessagesPageResponse(
    List<ChatSummary> Chats,
    List<MessageWithMetadata> Messages,
    PaginationInfo Pagination,
    long? SelectedChatId,
    MessagesPageUserContext UserContext
);

/// <summary>
/// User context for the Messages page (permission level, linked accounts, bot features).
/// Included in page response instead of separate auth/me call.
/// </summary>
public record MessagesPageUserContext(
    int PermissionLevel,
    List<long> LinkedTelegramIds,
    bool CanSendAsBot,
    string? LinkedUsername,
    long? BotUserId,
    string? BotFeatureUnavailableReason
);

/// <summary>
/// Chat summary for the sidebar (projection of ManagedChat).
/// </summary>
public record ChatSummary(
    long ChatId,
    string ChatName,
    string? ChatIconPath,
    int MessageCount,
    DateTimeOffset? LastMessageAt
);

/// <summary>
/// Message with metadata for display in the message list.
/// Bundles the message with related data from other tables.
/// </summary>
public record MessageWithMetadata(
    MessageRecord Message,
    int EditCount,
    ContentCheckSummary? LatestContentCheck,
    List<DetectionResultRecord>? DetectionResults = null,
    List<UserTag>? UserTags = null,
    List<AdminNote>? UserNotes = null
);

/// <summary>
/// Summary of content check for the spam badge (projection of DetectionResultRecord).
/// </summary>
public record ContentCheckSummary(
    bool IsSpam,
    int Confidence,
    string? Reason,
    DateTimeOffset CheckedAt
);

/// <summary>
/// Pagination metadata for paginated responses.
/// </summary>
public record PaginationInfo(
    int Page,
    int PageSize,
    int TotalCount,
    bool HasMore
);

/// <summary>
/// Aggregate response for the message detail dialog.
/// Bundles message with user info, detection history, and edit history.
/// </summary>
public record MessageDetailResponse(
    MessageRecord Message,
    TelegramUser? User,
    List<DetectionResultRecord> DetectionHistory,
    List<MessageEditRecord> EditHistory,
    List<UserTag>? UserTags,
    List<AdminNote>? UserNotes
);
