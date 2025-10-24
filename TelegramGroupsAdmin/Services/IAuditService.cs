using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Services;

public interface IAuditService
{
    Task LogEventAsync(
        AuditEventType eventType,
        string? actorUserId,
        string? targetUserId = null,
        string? value = null,
        CancellationToken ct = default);

    Task<List<AuditLogRecord>> GetRecentEventsAsync(int limit = 100, CancellationToken ct = default);
    Task<List<AuditLogRecord>> GetEventsForUserAsync(string userId, int limit = 100, CancellationToken ct = default);
    Task<List<AuditLogRecord>> GetEventsByActorAsync(string actorUserId, int limit = 100, CancellationToken ct = default);
    Task<List<AuditLogRecord>> GetEventsByTypeAsync(AuditEventType eventType, int limit = 100, CancellationToken ct = default);
    Task<(List<AuditLogRecord> Events, int TotalCount)> GetPagedEventsAsync(
        int skip,
        int take,
        AuditEventType? eventTypeFilter = null,
        string? actorUserIdFilter = null,
        string? targetUserIdFilter = null,
        CancellationToken ct = default);
}
