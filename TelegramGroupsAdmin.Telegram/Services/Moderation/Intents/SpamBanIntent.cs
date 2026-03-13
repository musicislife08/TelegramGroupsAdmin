using Telegram.Bot.Types;
using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation;

public sealed record SpamBanIntent : ModerationIntent
{
    public required int MessageId { get; init; }
    public required ChatIdentity Chat { get; init; }

    /// <summary>
    /// Optional Telegram Message object for rich notification content.
    /// </summary>
    public Message? TelegramMessage { get; init; }
}
