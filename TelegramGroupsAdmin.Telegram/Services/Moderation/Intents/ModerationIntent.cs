using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation;

/// <summary>
/// Base record for all moderation intents. Every moderation action carries
/// the target user identity, who initiated it, and why.
/// </summary>
public abstract record ModerationIntent
{
    /// <summary>
    /// Identity of the user being moderated. Constructed once at the call site
    /// and flows through the entire handler chain for logging without DB re-fetches.
    /// </summary>
    public required UserIdentity User { get; init; }

    /// <summary>
    /// Who initiated the moderation action (web user, Telegram user, or system actor).
    /// </summary>
    public required Actor Executor { get; init; }

    /// <summary>
    /// Human-readable reason for the action (stored in audit log).
    /// </summary>
    public required string Reason { get; init; }
}
