using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation;

public sealed record BanIntent : ModerationIntent
{
    public int? MessageId { get; init; }

    /// <summary>
    /// When set, enables ban celebration in this chat.
    /// </summary>
    public ChatIdentity? Chat { get; init; }
}
