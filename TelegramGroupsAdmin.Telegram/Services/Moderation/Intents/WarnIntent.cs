using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation;

public sealed record WarnIntent : ModerationIntent
{
    public int? MessageId { get; init; }
    public required ChatIdentity Chat { get; init; }
}
