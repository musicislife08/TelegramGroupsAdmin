using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Models;
using DataModels = TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.Repositories;

public class ReportsRepository : IReportsRepository
{
    private readonly AppDbContext _context;
    private readonly ILogger<ReportsRepository> _logger;

    public ReportsRepository(
        AppDbContext context,
        ILogger<ReportsRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<long> InsertAsync(Report report)
    {
        var entity = report.ToDataModel();
        _context.Reports.Add(entity);
        await _context.SaveChangesAsync();

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
        var entity = await _context.Reports
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
        var query = _context.Reports.AsNoTracking();

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
        var entity = await _context.Reports.FindAsync(reportId);

        if (entity != null)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            entity.Status = (DataModels.ReportStatus)(int)status;
            entity.ReviewedBy = reviewedBy;
            entity.ReviewedAt = now;
            entity.ActionTaken = actionTaken;
            entity.AdminNotes = adminNotes;

            await _context.SaveChangesAsync();

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
        var query = _context.Reports.AsNoTracking()
            .Where(r => r.Status == DataModels.ReportStatus.Pending);

        if (chatId.HasValue)
        {
            query = query.Where(r => r.ChatId == chatId.Value);
        }

        return await query.CountAsync();
    }

    public async Task DeleteOldReportsAsync(long olderThanTimestamp)
    {
        var toDelete = await _context.Reports
            .Where(r => r.ReportedAt < olderThanTimestamp
                && r.Status != DataModels.ReportStatus.Pending)
            .ToListAsync();

        var deleted = toDelete.Count;

        if (deleted > 0)
        {
            _context.Reports.RemoveRange(toDelete);
            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Deleted {Count} old reports (older than {Timestamp})",
                deleted,
                olderThanTimestamp);
        }
    }
}
