using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Services.Notifications;

/// <summary>
/// A single labeled field. When User is set, the value renders as a
/// text_mention entity in Telegram DMs — clickable regardless of whether
/// the user has interacted with the bot before.
/// </summary>
internal sealed record Field(string Label, string Value, UserIdentity? User = null);
