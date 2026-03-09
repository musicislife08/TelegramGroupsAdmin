using TelegramGroupsAdmin.Telegram.Constants;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Actions;

/// <summary>
/// Options for kick operations. Avoids bare bool/TimeSpan parameters on handler methods.
/// </summary>
public sealed record KickOptions
{
    /// <summary>
    /// How long the temporary ban lasts before Telegram auto-unbans.
    /// </summary>
    public TimeSpan Duration { get; init; } = ModerationConstants.DefaultKickDuration;

    /// <summary>
    /// Whether to revoke (delete) the user's recent messages on kick.
    /// True for welcome/exam kicks (cleanup join noise), false for admin/report kicks (preserve legitimate messages).
    /// </summary>
    public bool RevokeMessages { get; init; } = true;
}
