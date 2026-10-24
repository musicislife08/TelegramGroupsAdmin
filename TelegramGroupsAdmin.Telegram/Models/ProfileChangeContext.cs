using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Where a rename was observed, for the ProfileChange audit row the identity service writes after the
/// rename is recorded. Carries no message id: the message is stored after the user is observed.
/// </summary>
public sealed record ProfileChangeContext(ChatIdentity? Chat);
