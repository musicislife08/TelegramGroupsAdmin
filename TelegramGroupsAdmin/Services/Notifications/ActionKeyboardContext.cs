using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Services.Notifications;

/// <summary>
/// Context for building inline keyboard action buttons on notifications.
/// Reuses the existing ReportCallbackContext infrastructure.
/// </summary>
internal sealed record ActionKeyboardContext(
    long EntityId,
    long ChatId,
    long UserId,
    ReportType KeyboardType);
