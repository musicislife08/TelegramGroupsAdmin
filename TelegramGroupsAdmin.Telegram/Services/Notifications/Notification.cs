using TelegramGroupsAdmin.Core.Utilities;

namespace TelegramGroupsAdmin.Telegram.Services.Notifications;

/// <summary>
/// Notification message to be sent through a channel.
/// </summary>
/// <param name="Type">Notification type identifier (e.g., "warning", "mystatus", "welcome", "admin_report")</param>
/// <param name="Message">Plain-text fallback used when no entity payload is present, and for 403-queue replay.</param>
/// <param name="Telegram">Optional entity-based message. When set, the channel sends with entities (no parse_mode).
/// When null, the channel sends <see cref="Message"/> as plain text with an empty entity list.</param>
public record Notification(string Type, string Message, TelegramMessage? Telegram = null);
