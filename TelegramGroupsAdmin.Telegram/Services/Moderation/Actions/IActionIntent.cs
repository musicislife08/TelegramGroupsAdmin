using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Actions;

/// <summary>
/// Marker interface for moderation action intents.
/// Intents are immutable data objects representing a request to perform an action.
/// </summary>
public interface IActionIntent
{
    /// <summary>User ID the action targets.</summary>
    long UserId { get; }

    /// <summary>Who requested this action.</summary>
    Actor Executor { get; }

    /// <summary>Reason for the action (for audit/logging).</summary>
    string Reason { get; }
}
