using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation;

public sealed record RestrictIntent : ModerationIntent
{
    public required TimeSpan Duration { get; init; }
    public ChatIdentity? Chat { get; init; }
}
