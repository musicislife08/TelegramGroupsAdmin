using TelegramGroupsAdmin.Core.Models;
using DataModels = TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Core.Repositories;

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
    /// <param name="cancellationToken">Cancellation token</param>
    Task LogEventAsync(
        DataModels.AuditEventType eventType,
        Actor actor,
        Actor? target = null,
        string? value = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get recent audit events
    /// </summary>
    Task<List<AuditLogRecord>> GetRecentEventsAsync(int limit = QueryConstants.DefaultAuditLogLimit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get audit events for a specific user (as target)
    /// </summary>
    Task<List<AuditLogRecord>> GetEventsForUserAsync(string userId, int limit = QueryConstants.DefaultAuditLogLimit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get audit events by actor
    /// </summary>
    Task<List<AuditLogRecord>> GetEventsByActorAsync(string actorUserId, int limit = QueryConstants.DefaultAuditLogLimit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get audit events by event type
    /// </summary>
    Task<List<AuditLogRecord>> GetEventsByTypeAsync(DataModels.AuditEventType eventType, int limit = QueryConstants.DefaultAuditLogLimit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get paginated audit events with filtering
    /// </summary>
    Task<(List<AuditLogRecord> Events, int TotalCount)> GetPagedEventsAsync(
        int skip,
        int take,
        DataModels.AuditEventType? eventTypeFilter = null,
        string? actorUserIdFilter = null,
        string? targetUserIdFilter = null,
        CancellationToken cancellationToken = default);
}
