using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation;

public sealed record SyncBanIntent : ModerationIntent
{
    public required ChatIdentity Chat { get; init; }
    public int? TriggeredByMessageId { get; init; }
}
