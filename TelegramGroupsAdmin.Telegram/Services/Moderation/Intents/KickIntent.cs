using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation;

public sealed record KickIntent : ModerationIntent
{
    public required ChatIdentity Chat { get; init; }
}
