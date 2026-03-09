using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation;

public sealed record KickIntent : ModerationIntent
{
    public required ChatIdentity Chat { get; init; }

    /// <summary>
    /// Whether to revoke (delete) the user's recent messages on kick.
    /// True for welcome/exam kicks (cleanup join noise), false for admin/report kicks (preserve legitimate messages).
    /// </summary>
    public bool RevokeMessages { get; init; } = true;
}
