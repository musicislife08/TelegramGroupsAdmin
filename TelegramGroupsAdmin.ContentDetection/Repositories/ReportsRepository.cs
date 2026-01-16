using Microsoft.EntityFrameworkCore;
using TelegramGroupsAdmin.ContentDetection.Repositories.Mappings;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.ContentDetection.Models;
using DataModels = TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.ContentDetection.Repositories;

public class ReportsRepository : IReportsRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<ReportsRepository> _logger;

    public ReportsRepository(
        IDbContextFactory<AppDbContext> contextFactory,
        ILogger<ReportsRepository> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task<long> InsertAsync(Report report, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = report.ToDto();
        context.Reviews.Add(entity);
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Inserted report {ReportId} for message {MessageId} in chat {ChatId} by user {UserId}",
            entity.Id,
            report.MessageId,
            report.ChatId,
            report.ReportedByUserId);

        return entity.Id;
    }

    public async Task<Report?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await context.Reviews
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        return entity?.ToModel();
    }

    public async Task<List<Report>> GetPendingReportsAsync(long? chatId = null, CancellationToken cancellationToken = default)
    {
        return await GetReportsAsync(chatId, DataModels.ReportStatus.Pending, cancellationToken: cancellationToken);
    }

    public async Task<List<Report>> GetReportsAsync(
        long? chatId = null,
        DataModels.ReportStatus? status = null,
        int limit = 100,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var query = context.Reviews.AsNoTracking();

        if (chatId.HasValue)
        {
            query = query.Where(r => r.ChatId == chatId.Value);
        }

        if (status.HasValue)
        {
            query = query.Where(r => r.Status == (DataModels.ReportStatus)(int)status.Value);
        }

        var entities = await query
            .OrderByDescending(r => r.ReportedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return entities.Select(e => e.ToModel()).ToList();
    }

    public async Task UpdateReportStatusAsync(
        long reportId,
        DataModels.ReportStatus status,
        string reviewedBy,
        string actionTaken,
        string? adminNotes = null,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await context.Reviews.FindAsync([reportId], cancellationToken);

        if (entity != null)
        {
            var now = DateTimeOffset.UtcNow;

            entity.Status = (DataModels.ReportStatus)(int)status;
            entity.ReviewedBy = reviewedBy;
            entity.ReviewedAt = now;
            entity.ActionTaken = actionTaken;
            entity.AdminNotes = adminNotes;

            await context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Updated report {ReportId} to status {Status} by user {ReviewedBy} (action: {ActionTaken})",
                reportId,
                status,
                reviewedBy,
                actionTaken);
        }
    }

    public async Task<bool> TryUpdateReportStatusAsync(
        long reportId,
        DataModels.ReportStatus newStatus,
        string reviewedBy,
        string actionTaken,
        string? notes = null,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var rowsAffected = await context.Reviews
            .Where(r => r.Id == reportId && r.Status == DataModels.ReportStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, newStatus)
                .SetProperty(r => r.ReviewedBy, reviewedBy)
                .SetProperty(r => r.ActionTaken, actionTaken)
                .SetProperty(r => r.ReviewedAt, DateTimeOffset.UtcNow)
                .SetProperty(r => r.AdminNotes, notes),
                cancellationToken);

        if (rowsAffected > 0)
        {
            _logger.LogInformation(
                "Atomically updated report {ReportId} to status {Status} by {ReviewedBy} (action: {ActionTaken})",
                reportId,
                newStatus,
                reviewedBy,
                actionTaken);
        }

        return rowsAffected > 0;
    }

    public async Task<int> GetPendingCountAsync(long? chatId = null, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var query = context.Reviews.AsNoTracking()
            .Where(r => r.Status == DataModels.ReportStatus.Pending);

        if (chatId.HasValue)
        {
            query = query.Where(r => r.ChatId == chatId.Value);
        }

        return await query.CountAsync(cancellationToken);
    }

    public async Task<Report?> GetExistingPendingReportAsync(
        int messageId,
        long chatId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await context.Reviews
            .AsNoTracking()
            .FirstOrDefaultAsync(r =>
                r.MessageId == messageId &&
                r.ChatId == chatId &&
                r.Status == DataModels.ReportStatus.Pending,
                cancellationToken);

        return entity?.ToModel();
    }

    public async Task<int> DeleteOldReportsAsync(DateTimeOffset olderThanTimestamp, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var deleted = await context.Reviews
            .Where(r => r.ReportedAt < olderThanTimestamp
                && r.Status != DataModels.ReportStatus.Pending)
            .ExecuteDeleteAsync(cancellationToken);

        if (deleted > 0)
        {
            _logger.LogInformation(
                "Deleted {Count} old reports (older than {Timestamp})",
                deleted,
                olderThanTimestamp);
        }

        return deleted;
    }
}
