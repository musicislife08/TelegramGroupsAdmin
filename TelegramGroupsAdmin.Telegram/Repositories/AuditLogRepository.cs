using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data;
using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories;

public class AuditLogRepository : IAuditLogRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<AuditLogRepository> _logger;

    public AuditLogRepository(IDbContextFactory<AppDbContext> contextFactory, ILogger<AuditLogRepository> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task LogEventAsync(
        DataModels.AuditEventType eventType,
        string? actorUserId,
        string? targetUserId = null,
        string? value = null,
        CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        var entity = new DataModels.AuditLogRecordDto
        {
            EventType = eventType,
            Timestamp = DateTimeOffset.UtcNow,
            ActorUserId = actorUserId,
            TargetUserId = targetUserId,
            Value = value
        };

        context.AuditLogs.Add(entity);
        await context.SaveChangesAsync(ct);

        _logger.LogInformation("Audit log: {EventType} by {ActorUserId} on {TargetUserId}",
            eventType, actorUserId ?? "SYSTEM", targetUserId ?? "N/A");
    }

    public async Task<List<UiModels.AuditLogRecord>> GetRecentEventsAsync(int limit = 100, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        var entities = await context.AuditLogs
            .AsNoTracking()
            .OrderByDescending(al => al.Timestamp)
            .Take(limit)
            .ToListAsync(ct);

        return entities.Select(e => e.ToModel()).ToList();
    }

    public async Task<List<UiModels.AuditLogRecord>> GetEventsForUserAsync(string userId, int limit = 100, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        var entities = await context.AuditLogs
            .AsNoTracking()
            .Where(al => al.TargetUserId == userId)
            .OrderByDescending(al => al.Timestamp)
            .Take(limit)
            .ToListAsync(ct);

        return entities.Select(e => e.ToModel()).ToList();
    }

    public async Task<List<UiModels.AuditLogRecord>> GetEventsByActorAsync(string actorUserId, int limit = 100, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        var entities = await context.AuditLogs
            .AsNoTracking()
            .Where(al => al.ActorUserId == actorUserId)
            .OrderByDescending(al => al.Timestamp)
            .Take(limit)
            .ToListAsync(ct);

        return entities.Select(e => e.ToModel()).ToList();
    }

    public async Task<List<UiModels.AuditLogRecord>> GetEventsByTypeAsync(DataModels.AuditEventType eventType, int limit = 100, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        var entities = await context.AuditLogs
            .AsNoTracking()
            .Where(al => al.EventType == eventType)
            .OrderByDescending(al => al.Timestamp)
            .Take(limit)
            .ToListAsync(ct);

        return entities.Select(e => e.ToModel()).ToList();
    }

    public async Task<(List<UiModels.AuditLogRecord> Events, int TotalCount)> GetPagedEventsAsync(
        int skip,
        int take,
        DataModels.AuditEventType? eventTypeFilter = null,
        string? actorUserIdFilter = null,
        string? targetUserIdFilter = null,
        CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        // Build query with filters
        var query = context.AuditLogs.AsNoTracking();

        if (eventTypeFilter.HasValue)
        {
            query = query.Where(al => al.EventType == eventTypeFilter.Value);
        }

        if (!string.IsNullOrEmpty(actorUserIdFilter))
        {
            if (actorUserIdFilter == "SYSTEM")
            {
                query = query.Where(al => al.ActorUserId == null);
            }
            else
            {
                query = query.Where(al => al.ActorUserId == actorUserIdFilter);
            }
        }

        if (!string.IsNullOrEmpty(targetUserIdFilter))
        {
            query = query.Where(al => al.TargetUserId == targetUserIdFilter);
        }

        // Get total count for pagination
        var totalCount = await query.CountAsync(ct);

        // Get page of results
        var entities = await query
            .OrderByDescending(al => al.Timestamp)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

        var events = entities.Select(e => e.ToModel()).ToList();

        return (events, totalCount);
    }
}
