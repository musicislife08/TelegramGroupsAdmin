namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// Warning entry stored as JSONB in telegram_users.warnings column.
///
/// ARCHITECTURAL DECISION: Warnings stored as JSONB on user table rather than separate table.
///
/// Rationale:
/// - Warnings are always accessed WITH their user (never independently queried across all users at scale)
/// - Collection is bounded (90-day default expiry keeps array small)
/// - Homelab scale (~3k users max, typically &lt;10 warnings per user) makes JSONB efficient
/// - Single query to get user + all moderation state (no JOINs)
/// - Avoids FK constraint issues that complicated the separate table approach
/// - Cross-user aggregation (e.g., "users with warnings") uses WHERE warnings IS NOT NULL, then in-app counting
///
/// Trade-off accepted: Slightly slower cross-user warning aggregation (acceptable at homelab scale)
/// </summary>
public class WarningEntry
{
    /// <summary>When the warning was issued</summary>
    public DateTimeOffset IssuedAt { get; set; }

    /// <summary>When the warning expires (null = never expires)</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>Reason for the warning</summary>
    public string? Reason { get; set; }

    /// <summary>Actor type: "web_user", "telegram_user", or "system"</summary>
    public string ActorType { get; set; } = "system";

    /// <summary>Actor identifier (web user ID, telegram user ID, or system name)</summary>
    public string ActorId { get; set; } = "unknown";

    /// <summary>Context: Chat ID where warning was issued</summary>
    public long ChatId { get; set; }

    /// <summary>Context: Message ID that triggered the warning (optional)</summary>
    public int? MessageId { get; set; }
}
