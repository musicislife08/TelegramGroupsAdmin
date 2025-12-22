using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories;
using DataModels = TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Core.Services;

public class AuditService : IAuditService
{
    private readonly IAuditLogRepository _repository;

    public AuditService(IAuditLogRepository repository)
    {
        _repository = repository;
    }

    public Task LogEventAsync(
        AuditEventType eventType,
        Actor actor,
        Actor? target = null,
        string? value = null,
        CancellationToken cancellationToken = default)
    {
        return _repository.LogEventAsync((DataModels.AuditEventType)eventType, actor, target, value, cancellationToken);
    }

    public Task<List<AuditLogRecord>> GetRecentEventsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return _repository.GetRecentEventsAsync(limit, cancellationToken);
    }

    public Task<List<AuditLogRecord>> GetEventsForUserAsync(string userId, int limit = 100, CancellationToken cancellationToken = default)
    {
        return _repository.GetEventsForUserAsync(userId, limit, cancellationToken);
    }

    public Task<List<AuditLogRecord>> GetEventsByActorAsync(string actorUserId, int limit = 100, CancellationToken cancellationToken = default)
    {
        return _repository.GetEventsByActorAsync(actorUserId, limit, cancellationToken);
    }

    public Task<List<AuditLogRecord>> GetEventsByTypeAsync(AuditEventType eventType, int limit = 100, CancellationToken cancellationToken = default)
    {
        return _repository.GetEventsByTypeAsync((DataModels.AuditEventType)eventType, limit, cancellationToken);
    }

    public Task<(List<AuditLogRecord> Events, int TotalCount)> GetPagedEventsAsync(
        int skip,
        int take,
        AuditEventType? eventTypeFilter = null,
        string? actorUserIdFilter = null,
        string? targetUserIdFilter = null,
        CancellationToken cancellationToken = default)
    {
        return _repository.GetPagedEventsAsync(
            skip,
            take,
            eventTypeFilter.HasValue ? (DataModels.AuditEventType)eventTypeFilter.Value : null,
            actorUserIdFilter,
            targetUserIdFilter,
            cancellationToken);
    }
}
