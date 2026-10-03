using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Core.Utilities;

/// <summary>
/// Composes the human-readable description stored on a <c>user_actions</c> row. The audit
/// table is a log: the description is complete when written, using the same formatter as log
/// lines, and is never re-derived at read time. The Data layer stores <c>chat_id</c> only.
/// </summary>
public static class AuditReason
{
    /// <summary>
    /// Prefixes <paramref name="reason"/> with <c>[chat]</c> using the <see cref="CoreLoggingExtensions"/>
    /// <c>ToLogInfo()</c> log formatter (chat name, or <c>Chat {id}</c> when unnamed). Returns the reason
    /// unchanged when there is no chat, and the tag alone when the caller supplied no reason.
    /// </summary>
    public static string? WithChatTag(ChatIdentity? chat, string? reason)
    {
        if (chat is null) return reason;
        var tag = $"[{chat.ToLogInfo()}]";
        return string.IsNullOrWhiteSpace(reason) ? tag : $"{tag} {reason}";
    }
}
