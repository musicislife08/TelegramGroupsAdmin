using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data;
using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.Models;

namespace TelegramGroupsAdmin.Repositories;

public class AuditLogRepository
{
    private readonly AppDbContext _context;
    private readonly ILogger<AuditLogRepository> _logger;

    public AuditLogRepository(AppDbContext context, ILogger<AuditLogRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task LogEventAsync(
        DataModels.AuditEventType eventType,
        string? actorUserId,
        string? targetUserId = null,
        string? value = null,
        CancellationToken ct = default)
    {
        var entity = new DataModels.AuditLogRecord
        {
            EventType = eventType,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ActorUserId = actorUserId,
            TargetUserId = targetUserId,
            Value = value
        };

        _context.AuditLogs.Add(entity);
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation("Audit log: {EventType} by {ActorUserId} on {TargetUserId}",
            eventType, actorUserId ?? "SYSTEM", targetUserId ?? "N/A");
    }

    public async Task<List<UiModels.AuditLogRecord>> GetRecentEventsAsync(int limit = 100, CancellationToken ct = default)
    {
        var entities = await _context.AuditLogs
            .AsNoTracking()
            .OrderByDescending(al => al.Timestamp)
            .Take(limit)
            .ToListAsync(ct);

        return entities.Select(e => e.ToUiModel()).ToList();
    }

    public async Task<List<UiModels.AuditLogRecord>> GetEventsForUserAsync(string userId, int limit = 100, CancellationToken ct = default)
    {
        var entities = await _context.AuditLogs
            .AsNoTracking()
            .Where(al => al.TargetUserId == userId)
            .OrderByDescending(al => al.Timestamp)
            .Take(limit)
            .ToListAsync(ct);

        return entities.Select(e => e.ToUiModel()).ToList();
    }

    public async Task<List<UiModels.AuditLogRecord>> GetEventsByActorAsync(string actorUserId, int limit = 100, CancellationToken ct = default)
    {
        var entities = await _context.AuditLogs
            .AsNoTracking()
            .Where(al => al.ActorUserId == actorUserId)
            .OrderByDescending(al => al.Timestamp)
            .Take(limit)
            .ToListAsync(ct);

        return entities.Select(e => e.ToUiModel()).ToList();
    }

    public async Task<List<UiModels.AuditLogRecord>> GetEventsByTypeAsync(DataModels.AuditEventType eventType, int limit = 100, CancellationToken ct = default)
    {
        var entities = await _context.AuditLogs
            .AsNoTracking()
            .Where(al => al.EventType == eventType)
            .OrderByDescending(al => al.Timestamp)
            .Take(limit)
            .ToListAsync(ct);

        return entities.Select(e => e.ToUiModel()).ToList();
    }
}
