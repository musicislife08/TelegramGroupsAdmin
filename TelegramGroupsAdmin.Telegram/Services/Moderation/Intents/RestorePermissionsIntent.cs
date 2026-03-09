using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation;

public sealed record RestorePermissionsIntent : ModerationIntent
{
    public required ChatIdentity Chat { get; init; }
}
