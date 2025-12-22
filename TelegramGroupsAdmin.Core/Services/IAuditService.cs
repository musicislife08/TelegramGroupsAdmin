using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Core.Services;

public interface IAuditService
{
    /// <summary>
    /// Log an audit event with Actor exclusive arc pattern (ARCH-2 migration)
    /// </summary>
    Task LogEventAsync(
        AuditEventType eventType,
        Actor actor,
        Actor? target = null,
        string? value = null,
        CancellationToken cancellationToken = default);

    Task<List<AuditLogRecord>> GetRecentEventsAsync(int limit = 100, CancellationToken cancellationToken = default);
    Task<List<AuditLogRecord>> GetEventsForUserAsync(string userId, int limit = 100, CancellationToken cancellationToken = default);
    Task<List<AuditLogRecord>> GetEventsByActorAsync(string actorUserId, int limit = 100, CancellationToken cancellationToken = default);
    Task<List<AuditLogRecord>> GetEventsByTypeAsync(AuditEventType eventType, int limit = 100, CancellationToken cancellationToken = default);
    Task<(List<AuditLogRecord> Events, int TotalCount)> GetPagedEventsAsync(
        int skip,
        int take,
        AuditEventType? eventTypeFilter = null,
        string? actorUserIdFilter = null,
        string? targetUserIdFilter = null,
        CancellationToken cancellationToken = default);
}
