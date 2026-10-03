using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Context written into the ProfileChange audit row when a rename is recorded.
/// </summary>
public sealed record ProfileChangeContext(ChatIdentity? Chat, int? MessageId);
