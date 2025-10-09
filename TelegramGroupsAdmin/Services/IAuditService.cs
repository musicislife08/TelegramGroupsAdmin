using TelegramGroupsAdmin.Models;

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
}
