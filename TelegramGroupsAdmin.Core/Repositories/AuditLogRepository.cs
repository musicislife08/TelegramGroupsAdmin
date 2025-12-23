using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories.Mappings;
using TelegramGroupsAdmin.Data;
using DataModels = TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Core.Repositories;

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
        Actor actor,
        Actor? target = null,
        string? value = null,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = new DataModels.AuditLogRecordDto
        {
            EventType = eventType,
            Timestamp = DateTimeOffset.UtcNow,

            // Actor exclusive arc (ARCH-2)
            ActorWebUserId = actor.WebUserId,
            ActorTelegramUserId = actor.TelegramUserId,
            ActorSystemIdentifier = actor.SystemIdentifier,

            // Target exclusive arc (ARCH-2)
            TargetWebUserId = target?.WebUserId,
            TargetTelegramUserId = target?.TelegramUserId,
            TargetSystemIdentifier = target?.SystemIdentifier,

            Value = value
        };

        context.AuditLogs.Add(entity);
        await context.SaveChangesAsync(cancellationToken);

        // Format actor for logging
        var actorDisplay = actor.SystemIdentifier ?? actor.WebUserId ?? actor.TelegramUserId?.ToString() ?? "UNKNOWN";
        var targetDisplay = target?.SystemIdentifier ?? target?.WebUserId ?? target?.TelegramUserId?.ToString() ?? "N/A";

        _logger.LogInformation("Audit log: {EventType} by {Actor} on {Target}",
            eventType, actorDisplay, targetDisplay);
    }

    public async Task<List<AuditLogRecord>> GetRecentEventsAsync(int limit = QueryConstants.DefaultAuditLogLimit, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entities = await context.AuditLogs
            .AsNoTracking()
            .OrderByDescending(al => al.Timestamp)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return entities.Select(e => e.ToModel()).ToList();
    }

    public async Task<List<AuditLogRecord>> GetEventsForUserAsync(string userId, int limit = QueryConstants.DefaultAuditLogLimit, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entities = await context.AuditLogs
            .AsNoTracking()
            .Where(al => al.TargetWebUserId == userId)
            .OrderByDescending(al => al.Timestamp)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return entities.Select(e => e.ToModel()).ToList();
    }

    public async Task<List<AuditLogRecord>> GetEventsByActorAsync(string actorUserId, int limit = QueryConstants.DefaultAuditLogLimit, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entities = await context.AuditLogs
            .AsNoTracking()
            .Where(al => al.ActorWebUserId == actorUserId)
            .OrderByDescending(al => al.Timestamp)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return entities.Select(e => e.ToModel()).ToList();
    }

    public async Task<List<AuditLogRecord>> GetEventsByTypeAsync(DataModels.AuditEventType eventType, int limit = QueryConstants.DefaultAuditLogLimit, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entities = await context.AuditLogs
            .AsNoTracking()
            .Where(al => al.EventType == eventType)
            .OrderByDescending(al => al.Timestamp)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return entities.Select(e => e.ToModel()).ToList();
    }

    public async Task<(List<AuditLogRecord> Events, int TotalCount)> GetPagedEventsAsync(
        int skip,
        int take,
        DataModels.AuditEventType? eventTypeFilter = null,
        string? actorUserIdFilter = null,
        string? targetUserIdFilter = null,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

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
                // SYSTEM means any system actor (ActorSystemIdentifier is set)
                query = query.Where(al => al.ActorSystemIdentifier != null);
            }
            else
            {
                // Filter by web user ID
                query = query.Where(al => al.ActorWebUserId == actorUserIdFilter);
            }
        }

        if (!string.IsNullOrEmpty(targetUserIdFilter))
        {
            query = query.Where(al => al.TargetWebUserId == targetUserIdFilter);
        }

        // Get total count for pagination
        var totalCount = await query.CountAsync(cancellationToken);

        // Get page of results
        var entities = await query
            .OrderByDescending(al => al.Timestamp)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        var events = entities.Select(e => e.ToModel()).ToList();

        return (events, totalCount);
    }
}
