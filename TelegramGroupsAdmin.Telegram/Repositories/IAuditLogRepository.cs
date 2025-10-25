using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories;

/// <summary>
/// Repository for audit log operations
/// </summary>
public interface IAuditLogRepository
{
    /// <summary>
    /// Log an audit event with Actor exclusive arc pattern (ARCH-2 migration)
    /// </summary>
    /// <param name="eventType">Type of audit event</param>
    /// <param name="actor">Who performed the action (web user, telegram user, or system)</param>
    /// <param name="target">Who/what was affected (web user, telegram user, system, or null)</param>
    /// <param name="value">Additional context/data for the event</param>
    /// <param name="ct">Cancellation token</param>
    Task LogEventAsync(
        AuditEventType eventType,
        Actor actor,
        Actor? target = null,
        string? value = null,
        CancellationToken ct = default);

    /// <summary>
    /// Get recent audit events
    /// </summary>
    Task<List<UiModels.AuditLogRecord>> GetRecentEventsAsync(int limit = 100, CancellationToken ct = default);

    /// <summary>
    /// Get audit events for a specific user (as target)
    /// </summary>
    Task<List<UiModels.AuditLogRecord>> GetEventsForUserAsync(string userId, int limit = 100, CancellationToken ct = default);

    /// <summary>
    /// Get audit events by actor
    /// </summary>
    Task<List<UiModels.AuditLogRecord>> GetEventsByActorAsync(string actorUserId, int limit = 100, CancellationToken ct = default);

    /// <summary>
    /// Get audit events by event type
    /// </summary>
    Task<List<UiModels.AuditLogRecord>> GetEventsByTypeAsync(AuditEventType eventType, int limit = 100, CancellationToken ct = default);

    /// <summary>
    /// Get paginated audit events with filtering
    /// </summary>
    Task<(List<UiModels.AuditLogRecord> Events, int TotalCount)> GetPagedEventsAsync(
        int skip,
        int take,
        AuditEventType? eventTypeFilter = null,
        string? actorUserIdFilter = null,
        string? targetUserIdFilter = null,
        CancellationToken ct = default);
}
