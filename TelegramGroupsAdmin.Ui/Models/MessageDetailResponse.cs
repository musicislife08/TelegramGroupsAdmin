using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Ui.Models;

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
