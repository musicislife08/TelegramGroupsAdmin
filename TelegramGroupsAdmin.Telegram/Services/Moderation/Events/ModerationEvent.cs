using Telegram.Bot.Types;
using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Events;

/// <summary>
/// Event data for moderation actions, dispatched to handlers for side-effects.
/// </summary>
public record ModerationEvent
{
    /// <summary>Type of moderation action performed.</summary>
    public required ModerationActionType ActionType { get; init; }

    /// <summary>User ID the action was performed on.</summary>
    public required long UserId { get; init; }

    /// <summary>Who performed this action.</summary>
    public required Actor Executor { get; init; }

    /// <summary>Message ID involved (for spam/delete actions).</summary>
    public long? MessageId { get; init; }

    /// <summary>Chat ID where the action originated (for audit context).</summary>
    public long? ChatId { get; init; }

    /// <summary>Reason for the action (for audit log).</summary>
    public string? Reason { get; init; }

    /// <summary>Duration for temp bans/restrictions.</summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>When the action expires (for temp bans).</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>Telegram message object for backfill/training data.</summary>
    public Message? TelegramMessage { get; init; }

    /// <summary>Number of chats affected by cross-chat actions.</summary>
    public int ChatsAffected { get; init; }

    /// <summary>Whether trust was removed as part of this action.</summary>
    public bool TrustRemoved { get; init; }

    /// <summary>Whether a message was deleted as part of this action.</summary>
    public bool MessageDeleted { get; init; }

    /// <summary>Current warning count (for Warn actions).</summary>
    public int WarningCount { get; init; }
}

/// <summary>
/// Follow-up actions that handlers can request from the service.
/// </summary>
public enum ModerationFollowUp
{
    /// <summary>No follow-up needed.</summary>
    None,

    /// <summary>Handler requests a ban action (e.g., warning threshold exceeded).</summary>
    Ban
}

/// <summary>
/// Result from handler dispatch, containing any follow-up actions.
/// </summary>
public record ModerationDispatchResult
{
    /// <summary>Follow-up action requested by a handler.</summary>
    public ModerationFollowUp FollowUp { get; init; } = ModerationFollowUp.None;

    /// <summary>Reason for the follow-up (for logging/audit).</summary>
    public string? FollowUpReason { get; init; }
}
