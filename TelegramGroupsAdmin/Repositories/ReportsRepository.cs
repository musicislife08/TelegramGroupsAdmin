using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Models;
using DataModels = TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Repositories;

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

    public async Task<long> InsertAsync(Report report)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var entity = report.ToDataModel();
        context.Reports.Add(entity);
        await context.SaveChangesAsync();

        _logger.LogInformation(
            "Inserted report {ReportId} for message {MessageId} in chat {ChatId} by user {UserId}",
            entity.Id,
            report.MessageId,
            report.ChatId,
            report.ReportedByUserId);

        return entity.Id;
    }

    public async Task<Report?> GetByIdAsync(long id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var entity = await context.Reports
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id);

        return entity?.ToUiModel();
    }

    public async Task<List<Report>> GetPendingReportsAsync(long? chatId = null)
    {
        return await GetReportsAsync(chatId, ReportStatus.Pending);
    }

    public async Task<List<Report>> GetReportsAsync(
        long? chatId = null,
        ReportStatus? status = null,
        int limit = 100,
        int offset = 0)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

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
            .ToListAsync();

        return entities.Select(e => e.ToUiModel()).ToList();
    }

    public async Task UpdateReportStatusAsync(
        long reportId,
        ReportStatus status,
        string reviewedBy,
        string actionTaken,
        string? adminNotes = null)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var entity = await context.Reports.FindAsync(reportId);

        if (entity != null)
        {
            var now = DateTimeOffset.UtcNow;

            entity.Status = (DataModels.ReportStatus)(int)status;
            entity.ReviewedBy = reviewedBy;
            entity.ReviewedAt = now;
            entity.ActionTaken = actionTaken;
            entity.AdminNotes = adminNotes;

            await context.SaveChangesAsync();

            _logger.LogInformation(
                "Updated report {ReportId} to status {Status} by user {ReviewedBy} (action: {ActionTaken})",
                reportId,
                status,
                reviewedBy,
                actionTaken);
        }
    }

    public async Task<int> GetPendingCountAsync(long? chatId = null)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var query = context.Reports.AsNoTracking()
            .Where(r => r.Status == DataModels.ReportStatus.Pending);

        if (chatId.HasValue)
        {
            query = query.Where(r => r.ChatId == chatId.Value);
        }

        return await query.CountAsync();
    }

    public async Task DeleteOldReportsAsync(DateTimeOffset olderThanTimestamp)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var toDelete = await context.Reports
            .Where(r => r.ReportedAt < olderThanTimestamp
                && r.Status != DataModels.ReportStatus.Pending)
            .ToListAsync();

        var deleted = toDelete.Count;

        if (deleted > 0)
        {
            context.Reports.RemoveRange(toDelete);
            await context.SaveChangesAsync();

            _logger.LogInformation(
                "Deleted {Count} old reports (older than {Timestamp})",
                deleted,
                olderThanTimestamp);
        }
    }
}
