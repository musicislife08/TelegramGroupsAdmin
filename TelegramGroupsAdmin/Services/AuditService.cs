using TelegramGroupsAdmin.Data.Models;
using TelegramGroupsAdmin.Data.Repositories;

namespace TelegramGroupsAdmin.Services;

public class AuditService : IAuditService
{
    private readonly AuditLogRepository _repository;

    public AuditService(AuditLogRepository repository)
    {
        _repository = repository;
    }

    public Task LogEventAsync(
        AuditEventType eventType,
        string? actorUserId,
        string? targetUserId = null,
        string? value = null,
        CancellationToken ct = default)
    {
        return _repository.LogEventAsync(eventType, actorUserId, targetUserId, value, ct);
    }

    public Task<List<AuditLogRecord>> GetRecentEventsAsync(int limit = 100, CancellationToken ct = default)
    {
        return _repository.GetRecentEventsAsync(limit, ct);
    }

    public Task<List<AuditLogRecord>> GetEventsForUserAsync(string userId, int limit = 100, CancellationToken ct = default)
    {
        return _repository.GetEventsForUserAsync(userId, limit, ct);
    }

    public Task<List<AuditLogRecord>> GetEventsByActorAsync(string actorUserId, int limit = 100, CancellationToken ct = default)
    {
        return _repository.GetEventsByActorAsync(actorUserId, limit, ct);
    }

    public Task<List<AuditLogRecord>> GetEventsByTypeAsync(AuditEventType eventType, int limit = 100, CancellationToken ct = default)
    {
        return _repository.GetEventsByTypeAsync(eventType, limit, ct);
    }
}
