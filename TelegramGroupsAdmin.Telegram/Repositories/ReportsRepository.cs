using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Telegram.Models;
using DataModels = TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories;

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
        context.Reports.Add(entity);
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

        var entity = await context.Reports
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        return entity?.ToModel();
    }

    public async Task<List<Report>> GetPendingReportsAsync(long? chatId = null, CancellationToken cancellationToken = default)
    {
        return await GetReportsAsync(chatId, ReportStatus.Pending, cancellationToken: cancellationToken);
    }

    public async Task<List<Report>> GetReportsAsync(
        long? chatId = null,
        ReportStatus? status = null,
        int limit = 100,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var query = context.Reports.AsNoTracking();

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
        ReportStatus status,
        string reviewedBy,
        string actionTaken,
        string? adminNotes = null,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await context.Reports.FindAsync(new object[] { reportId }, cancellationToken);

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

    public async Task<int> GetPendingCountAsync(long? chatId = null, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var query = context.Reports.AsNoTracking()
            .Where(r => r.Status == DataModels.ReportStatus.Pending);

        if (chatId.HasValue)
        {
            query = query.Where(r => r.ChatId == chatId.Value);
        }

        return await query.CountAsync(cancellationToken);
    }

    public async Task DeleteOldReportsAsync(DateTimeOffset olderThanTimestamp, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var toDelete = await context.Reports
            .Where(r => r.ReportedAt < olderThanTimestamp
                && r.Status != DataModels.ReportStatus.Pending)
            .ToListAsync(cancellationToken);

        var deleted = toDelete.Count;

        if (deleted > 0)
        {
            context.Reports.RemoveRange(toDelete);
            await context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Deleted {Count} old reports (older than {Timestamp})",
                deleted,
                olderThanTimestamp);
        }
    }
}
