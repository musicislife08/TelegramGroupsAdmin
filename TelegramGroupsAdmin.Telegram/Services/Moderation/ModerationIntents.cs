using Telegram.Bot.Types;
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

// ═══════════════════════════════════════════════════════════════════════════════
// Spam / Ban Intents
// ═══════════════════════════════════════════════════════════════════════════════

public record SpamBanIntent : ModerationIntent
{
    public required long MessageId { get; init; }
    public required ChatIdentity Chat { get; init; }

    /// <summary>
    /// Optional Telegram Message object for rich notification content.
    /// </summary>
    public Message? TelegramMessage { get; init; }
}

public record BanIntent : ModerationIntent
{
    public long? MessageId { get; init; }

    /// <summary>
    /// When set, enables ban celebration in this chat.
    /// </summary>
    public ChatIdentity? Chat { get; init; }
}

public record SyncBanIntent : ModerationIntent
{
    public required ChatIdentity Chat { get; init; }
    public long? TriggeredByMessageId { get; init; }
}

public record TempBanIntent : ModerationIntent
{
    public long? MessageId { get; init; }
    public required TimeSpan Duration { get; init; }
}

public record UnbanIntent : ModerationIntent
{
    public bool RestoreTrust { get; init; }
}

// ═══════════════════════════════════════════════════════════════════════════════
// Warning / Trust Intents
// ═══════════════════════════════════════════════════════════════════════════════

public record WarnIntent : ModerationIntent
{
    public long? MessageId { get; init; }
    public required ChatIdentity Chat { get; init; }
}

public record TrustIntent : ModerationIntent;

public record UntrustIntent : ModerationIntent;

// ═══════════════════════════════════════════════════════════════════════════════
// Message / Restriction Intents
// ═══════════════════════════════════════════════════════════════════════════════

public record DeleteMessageIntent : ModerationIntent
{
    public required long MessageId { get; init; }
    public required ChatIdentity Chat { get; init; }
}

public record RestrictIntent : ModerationIntent
{
    public long? MessageId { get; init; }
    public required TimeSpan Duration { get; init; }
    public ChatIdentity? Chat { get; init; }
}

// ═══════════════════════════════════════════════════════════════════════════════
// Chat Management Intents
// ═══════════════════════════════════════════════════════════════════════════════

public record RestorePermissionsIntent : ModerationIntent
{
    public required ChatIdentity Chat { get; init; }
}

public record KickIntent : ModerationIntent
{
    public required ChatIdentity Chat { get; init; }
}

// ═══════════════════════════════════════════════════════════════════════════════
// Violation Intents (automated detection)
// ═══════════════════════════════════════════════════════════════════════════════

public record MalwareViolationIntent : ModerationIntent
{
    public required long MessageId { get; init; }
    public required ChatIdentity Chat { get; init; }
    public required string MalwareDetails { get; init; }

    /// <summary>
    /// Optional Telegram Message object for rich notification content.
    /// </summary>
    public Message? TelegramMessage { get; init; }
}

public record CriticalViolationIntent : ModerationIntent
{
    public required long MessageId { get; init; }
    public required ChatIdentity Chat { get; init; }
    public required List<string> Violations { get; init; }

    /// <summary>
    /// Optional Telegram Message object for rich notification content.
    /// </summary>
    public Message? TelegramMessage { get; init; }
}
