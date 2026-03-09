using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation;

public sealed record DeleteMessageIntent : ModerationIntent
{
    public required int MessageId { get; init; }
    public required ChatIdentity Chat { get; init; }
}
