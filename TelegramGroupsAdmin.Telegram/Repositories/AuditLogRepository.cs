using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data;
using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories;

public class AuditLogRepository
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
        await using var context = await _contextFactory.CreateDbContextAsync();

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
        await using var context = await _contextFactory.CreateDbContextAsync();

        var entities = await context.AuditLogs
            .AsNoTracking()
            .OrderByDescending(al => al.Timestamp)
            .Take(limit)
            .ToListAsync(ct);

        return entities.Select(e => e.ToUiModel()).ToList();
    }

    public async Task<List<UiModels.AuditLogRecord>> GetEventsForUserAsync(string userId, int limit = 100, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var entities = await context.AuditLogs
            .AsNoTracking()
            .Where(al => al.TargetUserId == userId)
            .OrderByDescending(al => al.Timestamp)
            .Take(limit)
            .ToListAsync(ct);

        return entities.Select(e => e.ToUiModel()).ToList();
    }

    public async Task<List<UiModels.AuditLogRecord>> GetEventsByActorAsync(string actorUserId, int limit = 100, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var entities = await context.AuditLogs
            .AsNoTracking()
            .Where(al => al.ActorUserId == actorUserId)
            .OrderByDescending(al => al.Timestamp)
            .Take(limit)
            .ToListAsync(ct);

        return entities.Select(e => e.ToUiModel()).ToList();
    }

    public async Task<List<UiModels.AuditLogRecord>> GetEventsByTypeAsync(DataModels.AuditEventType eventType, int limit = 100, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var entities = await context.AuditLogs
            .AsNoTracking()
            .Where(al => al.EventType == eventType)
            .OrderByDescending(al => al.Timestamp)
            .Take(limit)
            .ToListAsync(ct);

        return entities.Select(e => e.ToUiModel()).ToList();
    }
}
