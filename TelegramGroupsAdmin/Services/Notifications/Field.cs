namespace TelegramGroupsAdmin.Services.Notifications;

/// <summary>
/// A single labeled field. When TelegramUserId is set, the value renders as a
/// clickable tg://user deep link in Telegram DMs.
/// </summary>
internal sealed record Field(string Label, string Value, long? TelegramUserId = null);
