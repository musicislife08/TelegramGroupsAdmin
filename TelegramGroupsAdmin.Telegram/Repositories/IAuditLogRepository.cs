using TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories;

/// <summary>
/// Repository for audit log operations
/// </summary>
public interface IAuditLogRepository
{
    /// <summary>
    /// Log an audit event
    /// </summary>
    Task LogEventAsync(
        AuditEventType eventType,
        string? actorUserId,
        string? targetUserId = null,
        string? value = null,
        CancellationToken ct = default);

    /// <summary>
    /// Get recent audit events
    /// </summary>
    Task<List<UiModels.AuditLogRecord>> GetRecentEventsAsync(int limit = 100, CancellationToken ct = default);

    /// <summary>
    /// Get audit events for a specific user (as target)
    /// </summary>
    Task<List<UiModels.AuditLogRecord>> GetEventsForUserAsync(string userId, int limit = 100, CancellationToken ct = default);

    /// <summary>
    /// Get audit events by actor
    /// </summary>
    Task<List<UiModels.AuditLogRecord>> GetEventsByActorAsync(string actorUserId, int limit = 100, CancellationToken ct = default);

    /// <summary>
    /// Get audit events by event type
    /// </summary>
    Task<List<UiModels.AuditLogRecord>> GetEventsByTypeAsync(AuditEventType eventType, int limit = 100, CancellationToken ct = default);
}
