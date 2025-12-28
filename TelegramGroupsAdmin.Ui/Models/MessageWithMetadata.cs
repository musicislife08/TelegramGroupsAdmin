using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Ui.Models;

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
