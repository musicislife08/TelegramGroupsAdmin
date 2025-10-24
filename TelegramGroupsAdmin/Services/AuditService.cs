using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using DataModels = TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Services;

public class AuditService : IAuditService
{
    private readonly IAuditLogRepository _repository;

    public AuditService(IAuditLogRepository repository)
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
        return _repository.LogEventAsync((DataModels.AuditEventType)eventType, actorUserId, targetUserId, value, ct);
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
        return _repository.GetEventsByTypeAsync((DataModels.AuditEventType)eventType, limit, ct);
    }

    public Task<(List<AuditLogRecord> Events, int TotalCount)> GetPagedEventsAsync(
        int skip,
        int take,
        AuditEventType? eventTypeFilter = null,
        string? actorUserIdFilter = null,
        string? targetUserIdFilter = null,
        CancellationToken ct = default)
    {
        return _repository.GetPagedEventsAsync(
            skip,
            take,
            eventTypeFilter.HasValue ? (DataModels.AuditEventType)eventTypeFilter.Value : null,
            actorUserIdFilter,
            targetUserIdFilter,
            ct);
    }
}
