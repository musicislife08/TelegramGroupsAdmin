namespace TelegramGroupsAdmin.Telegram.Services.Notifications;

/// <summary>
/// Notification message to be sent through a channel
/// </summary>
/// <param name="Type">Notification type identifier (e.g., "warning", "mystatus", "welcome", "admin_report")</param>
/// <param name="Message">The formatted message text to send</param>
public record Notification(string Type, string Message);
