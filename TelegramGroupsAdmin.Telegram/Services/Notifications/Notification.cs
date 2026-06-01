using TelegramGroupsAdmin.Core.Utilities;

namespace TelegramGroupsAdmin.Telegram.Services.Notifications;

/// <summary>
/// Notification message to be sent through a channel.
/// </summary>
/// <param name="Type">Notification type identifier (e.g., "warning", "mystatus", "welcome", "admin_report")</param>
/// <param name="Message">Rendered message — text plus explicit entities. Sent with no parse_mode.
/// Use <see cref="TelegramMessage.Plain(string)"/> for a text-only message with no entities.</param>
public record Notification(string Type, TelegramMessage Message);
