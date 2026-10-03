namespace TelegramGroupsAdmin.Services.Notifications;

/// <summary>
/// An animation (GIF) attached to a notification: the full path on disk for uploads and an
/// optional cached Telegram file_id that is preferred when present.
/// </summary>
internal sealed record NotificationAnimation(string Path, string? FileId);
